using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace NsdRpc;

/// <summary>
/// One server endpoint: one peer per accepted connection plus the handler
/// dispatcher. Start launches the accept loop.
/// </summary>
public sealed class RpcServer : IRpcHost
{
    private readonly IRpcServerTransport _transport;
    private readonly RpcRegistry _registry;
    private readonly RpcOptions _options;
    private readonly ConcurrentDictionary<ulong, RpcPeer> _peers = new();
    private volatile bool _closed;
    private Task? _acceptLoop;

    public RpcServer(IRpcServerTransport transport, RpcRegistry registry, RpcOptions? options = null)
    {
        if (transport == null)
        {
            throw new ArgumentNullException(nameof(transport));
        }

        if (registry == null)
        {
            throw new ArgumentNullException(nameof(registry));
        }

        _transport = transport;
        _registry = registry;
        _options = options ?? RpcOptions.Default;
    }

    /// <summary>Starts the accept loop. Call once.</summary>
    public void Start()
    {
        if (_acceptLoop != null)
        {
            throw new InvalidOperationException("The server already started.");
        }

        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    /// <summary>Fires after the accept loop wires one peer.</summary>
    public event Action<RpcPeer>? ClientConnected;

    /// <summary>Fires when a peer leaves, through a disconnect, a prune, or a close.</summary>
    public event Action<RpcPeer>? ClientDisconnected;

    /// <summary>The peer for one client, or null for an unknown client id.</summary>
    public RpcPeer? GetPeer(ulong clientId)
    {
        return _peers.TryGetValue(clientId, out RpcPeer? peer) ? peer : null;
    }

    /// <summary>The live peers. A snapshot.</summary>
    public IReadOnlyCollection<RpcPeer> Peers => SnapshotPeers();

    /// <summary>
    /// Invokes every peer. The first failure wins. With no failure, the last
    /// success wins. No peers fails with <see cref="RpcErrorCode.Unavailable"/>.
    /// </summary>
    public Task<RpcResult<TResponse>> InvokeAsync<TRequest, TResponse>(
        ulong method,
        in TRequest request,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where TRequest : struct
        where TResponse : struct
    {
        RpcPeer[] peers = SnapshotPeers();
        if (peers.Length == 0)
        {
            return NoPeers<TResponse>();
        }

        return AggregateAsync<TRequest, TResponse>(peers, method, request, timeout, cancellationToken);
    }

    /// <summary>
    /// Invokes every peer and fans every chunk into one sink. The aggregation
    /// matches <see cref="InvokeAsync"/>.
    /// </summary>
    public Task<RpcResult<TResponse>> InvokeStreamAsync<TRequest, TChunk, TResponse>(
        ulong method,
        in TRequest request,
        IChunkSink<TChunk> sink,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where TRequest : struct
        where TChunk : struct
        where TResponse : struct
    {
        if (sink == null)
        {
            throw new ArgumentNullException(nameof(sink));
        }

        RpcPeer[] peers = SnapshotPeers();
        if (peers.Length == 0)
        {
            return NoPeers<TResponse>();
        }

        return AggregateStreamAsync<TRequest, TChunk, TResponse>(
            peers, method, request, sink, timeout, cancellationToken);
    }

    /// <summary>
    /// Invokes every peer and returns a session that merges every chunk. The
    /// response aggregates like <see cref="InvokeAsync"/>.
    /// </summary>
    public RpcStreamSession<TChunk, TResponse> InvokeStreamAsync<TRequest, TChunk, TResponse>(
        ulong method,
        in TRequest request,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where TRequest : struct
        where TChunk : struct
        where TResponse : struct
    {
        var queue = new AsyncChunkQueue<TChunk>(_options.MaxBufferedChunks);
        var sink = new SessionChunkSink<TChunk>(queue);
        Task<RpcResult<TResponse>> response = InvokeStreamAsync<TRequest, TChunk, TResponse>(
            method, in request, sink, timeout, cancellationToken);
        return new RpcStreamSession<TChunk, TResponse>(response, queue);
    }

    /// <summary>
    /// Notifies every peer. Returns true when every peer accepted. No peers
    /// returns false.
    /// </summary>
    public bool Notify<TRequest>(ulong method, in TRequest request)
        where TRequest : struct
    {
        RpcPeer[] peers = SnapshotPeers();
        if (peers.Length == 0)
        {
            return false;
        }

        bool accepted = true;
        for (int index = 0; index < peers.Length; index++)
        {
            if (!peers[index].Notify(method, in request))
            {
                accepted = false;
            }
        }

        return accepted;
    }

    /// <summary>
    /// Notifies every peer. Returns true when every peer accepted. No peers
    /// returns false.
    /// </summary>
    public bool Notify(ulong method)
    {
        RpcPeer[] peers = SnapshotPeers();
        if (peers.Length == 0)
        {
            return false;
        }

        bool accepted = true;
        for (int index = 0; index < peers.Length; index++)
        {
            if (!peers[index].Notify(method))
            {
                accepted = false;
            }
        }

        return accepted;
    }

    public void CheckTimeouts(long timestampNanos)
    {
        foreach (KeyValuePair<ulong, RpcPeer> pair in _peers)
        {
            RpcPeer peer = pair.Value;
            peer.CheckTimeouts(timestampNanos);
            if (peer.Connection.IsConnected)
            {
                continue;
            }

            RemovePeer(pair.Key);
        }
    }

    public void Poll()
    {
        CheckTimeouts(_options.TimestampNanos());
    }

    /// <summary>Stops accepting and faults every pending call.</summary>
    public void Close()
    {
        _closed = true;
        _transport.Close();
        foreach (KeyValuePair<ulong, RpcPeer> pair in _peers)
        {
            pair.Value.FaultPending((int)RpcErrorCode.Unavailable, "server closed");
            ClientDisconnected?.Invoke(pair.Value);
        }

        _peers.Clear();
    }

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "The accept loop survives a broken transport. It is a module boundary.")]
    private async Task AcceptLoopAsync()
    {
        while (!_closed)
        {
            IServerConnection? connection;
            try
            {
                connection = await _transport.AcceptAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                RpcLog.Error("accept failed", exception);
                break;
            }

            if (connection == null)
            {
                break;
            }

            if (_closed)
            {
                break;
            }

            Invariant.That(connection.ClientId != 0UL, "The server transport assigned client id zero.");
            var peer = new RpcPeer(connection, _options);
            peer.DispatchHook = new RpcDispatcher(_registry, peer).DispatchAsync;
            Invariant.That(_peers.TryAdd(connection.ClientId, peer), "The server transport assigned a duplicate client id.");
            ulong clientId = connection.ClientId;
            connection.Disconnected += () => RemovePeer(clientId);
            ClientConnected?.Invoke(peer);
        }
    }

    private void RemovePeer(ulong clientId)
    {
        if (_peers.TryRemove(clientId, out RpcPeer? peer))
        {
            peer.FaultPending((int)RpcErrorCode.Unavailable, "connection closed");
            ClientDisconnected?.Invoke(peer);
        }
    }

    private RpcPeer[] SnapshotPeers()
    {
        var peers = new List<RpcPeer>(_peers.Count);
        foreach (KeyValuePair<ulong, RpcPeer> pair in _peers)
        {
            peers.Add(pair.Value);
        }

        return peers.ToArray();
    }

    private static Task<RpcResult<TResponse>> NoPeers<TResponse>()
        where TResponse : struct
    {
        return Task.FromResult(
            RpcResult<TResponse>.Failure((int)RpcErrorCode.Unavailable, "no peers"));
    }

    private static async Task<RpcResult<TResponse>> AggregateAsync<TRequest, TResponse>(
        RpcPeer[] peers,
        ulong method,
        TRequest request,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
        where TRequest : struct
        where TResponse : struct
    {
        var calls = new Task<RpcResult<TResponse>>[peers.Length];
        for (int index = 0; index < peers.Length; index++)
        {
            calls[index] = peers[index].InvokeAsync<TRequest, TResponse>(
                method, in request, timeout, cancellationToken);
        }

        RpcResult<TResponse> lastSuccess = default;
        for (int index = 0; index < calls.Length; index++)
        {
            RpcResult<TResponse> result = await calls[index].ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return result;
            }

            lastSuccess = result;
        }

        return lastSuccess;
    }

    private static async Task<RpcResult<TResponse>> AggregateStreamAsync<TRequest, TChunk, TResponse>(
        RpcPeer[] peers,
        ulong method,
        TRequest request,
        IChunkSink<TChunk> sink,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
        where TRequest : struct
        where TChunk : struct
        where TResponse : struct
    {
        var calls = new Task<RpcResult<TResponse>>[peers.Length];
        for (int index = 0; index < peers.Length; index++)
        {
            calls[index] = peers[index].InvokeStreamAsync<TRequest, TChunk, TResponse>(
                method, in request, sink, timeout, cancellationToken);
        }

        RpcResult<TResponse> lastSuccess = default;
        for (int index = 0; index < calls.Length; index++)
        {
            RpcResult<TResponse> result = await calls[index].ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return result;
            }

            lastSuccess = result;
        }

        return lastSuccess;
    }
}
