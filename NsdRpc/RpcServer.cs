using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace NsdRpc;

/// <summary>
/// One server endpoint: one peer per accepted connection plus the handler
/// dispatcher. Start launches the accept loop.
/// </summary>
public sealed class RpcServer
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
}
