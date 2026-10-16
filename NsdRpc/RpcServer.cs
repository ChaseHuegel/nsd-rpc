using System.Diagnostics.CodeAnalysis;

namespace NsdRpc;

/// <summary>
/// One server endpoint: one peer per accepted connection plus the handler
/// dispatcher. Start launches the accept loop. Select a peer from
/// <see cref="Peers"/> or <see cref="GetPeer"/> and call it.
/// </summary>
public sealed class RpcServer : IRpcHost
{
    private readonly IRpcServerTransport _transport;
    private readonly RpcRegistry _registry;
    private readonly RpcOptions _options;
    private readonly object _gate = new();
    private readonly Dictionary<ulong, RpcPeer> _peers = new();
    private volatile RpcPeer[] _snapshot = [];
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
        lock (_gate)
        {
            return _peers.TryGetValue(clientId, out RpcPeer? peer) ? peer : null;
        }
    }

    /// <summary>The live peers. An immutable snapshot.</summary>
    public PeerSet Peers => new(_snapshot);

    public void CheckTimeouts(long timestampNanos)
    {
        foreach (RpcPeer peer in _snapshot)
        {
            peer.CheckTimeouts(timestampNanos);
            if (!peer.Connection.IsConnected)
            {
                RemovePeer(peer.ClientId);
            }
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

        RpcPeer[] peers;
        lock (_gate)
        {
            peers = _snapshot;
            _peers.Clear();
            _snapshot = [];
        }

        for (int index = 0; index < peers.Length; index++)
        {
            peers[index].FaultPending((int)RpcErrorCode.Unavailable, "server closed");
            ClientDisconnected?.Invoke(peers[index]);
        }
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
            peer.Host = this;
            ulong clientId = connection.ClientId;
            lock (_gate)
            {
                Invariant.That(
                    !_peers.ContainsKey(clientId),
                    "The server transport assigned a duplicate client id.");
                _peers.Add(clientId, peer);
                _snapshot = BuildSnapshot();
            }

            connection.Disconnected += () => RemovePeer(clientId);
            ClientConnected?.Invoke(peer);
        }
    }

    private void RemovePeer(ulong clientId)
    {
        RpcPeer? peer;
        lock (_gate)
        {
            if (!_peers.Remove(clientId, out peer))
            {
                return;
            }

            _snapshot = BuildSnapshot();
        }

        peer.FaultPending((int)RpcErrorCode.Unavailable, "connection closed");
        ClientDisconnected?.Invoke(peer);
    }

    // Builds the immutable peer array. Call under _gate.
    private RpcPeer[] BuildSnapshot()
    {
        var peers = new RpcPeer[_peers.Count];
        int index = 0;
        foreach (KeyValuePair<ulong, RpcPeer> pair in _peers)
        {
            peers[index] = pair.Value;
            index++;
        }

        return peers;
    }
}
