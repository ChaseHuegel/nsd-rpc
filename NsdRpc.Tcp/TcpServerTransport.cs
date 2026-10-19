using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using NsdRpc;

namespace NsdRpc.Tcp;

/// <summary>
/// A TCP listener. Accept one connection per AcceptAsync call. Each handshake
/// runs in the background, so a slow client never stalls the other clients.
/// </summary>
public sealed class TcpServerTransport : IRpcServerTransport
{
    private readonly RpcOptions _rpcOptions;
    private readonly TcpOptions _tcpOptions;
    private readonly object _listenerLock = new();
    private readonly object _acceptedLock = new();
    private readonly List<TcpServerConnection> _accepted = [];
    private readonly object _readyLock = new();
    private readonly Queue<TcpServerConnection> _ready = new();
    private readonly SemaphoreSlim _readySignal = new(0);
    private TcpListener? _listener;
    private bool _listenerStarted;
    private Task? _acceptLoop;
    private ulong _nextClientId = 1;
    private volatile bool _closed;

    public TcpServerTransport(RpcOptions rpcOptions, TcpOptions tcpOptions)
    {
        _rpcOptions = rpcOptions ?? throw new ArgumentNullException(nameof(rpcOptions));
        _tcpOptions = tcpOptions ?? throw new ArgumentNullException(nameof(tcpOptions));
    }

    /// <summary>The bound port. Useful when TcpOptions.Port is zero.</summary>
    public int Port => ((IPEndPoint)EnsureListener().LocalEndpoint).Port;

    // The live accepted connections. Tests use it to prove the list stays
    // bounded.
    internal int AcceptedConnectionCount
    {
        get
        {
            lock (_acceptedLock)
            {
                return _accepted.Count;
            }
        }
    }

    public async Task<IServerConnection?> AcceptAsync()
    {
        StartAccepting();
        while (true)
        {
            if (_closed)
            {
                return null;
            }

            await _readySignal.WaitAsync().ConfigureAwait(false);
            if (_closed)
            {
                return null;
            }

            lock (_readyLock)
            {
                if (_ready.Count > 0)
                {
                    return _ready.Dequeue();
                }
            }
        }
    }

    public void Close()
    {
        _closed = true;
        lock (_listenerLock)
        {
            _listener?.Stop();
        }

        TcpServerConnection[] connections;
        lock (_acceptedLock)
        {
            connections = _accepted.ToArray();
            _accepted.Clear();
        }

        foreach (TcpServerConnection connection in connections)
        {
            connection.Close();
        }

        // Wake a waiting AcceptAsync so it observes the close.
        _readySignal.Release();
    }

    private void StartAccepting()
    {
        lock (_listenerLock)
        {
            if (_acceptLoop == null)
            {
                _acceptLoop = Task.Run(AcceptLoopAsync);
            }
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "A failed accept ends the loop. It is a transport boundary.")]
    private async Task AcceptLoopAsync()
    {
        while (!_closed)
        {
            Socket socket;
            try
            {
                socket = await EnsureListener().AcceptSocketAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                break;
            }

            if (_closed)
            {
                socket.Dispose();
                break;
            }

            socket.NoDelay = _tcpOptions.NoDelay;
            ulong clientId = checked(_nextClientId++);
            var connection = new TcpServerConnection(
                socket, _rpcOptions, _tcpOptions, clientId, socket.RemoteEndPoint);
            // A disconnect removes the connection from the list. The list stays
            // bounded by the live connection count.
            connection.Disconnected += _ => RemoveAccepted(connection);
            lock (_acceptedLock)
            {
                _accepted.Add(connection);
            }

            _ = CompleteHandshakeAsync(connection);
        }

        // Wake a waiting AcceptAsync so it observes the close.
        _readySignal.Release();
    }

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "A failed handshake skips one connection. It is a transport boundary.")]
    private async Task CompleteHandshakeAsync(TcpServerConnection connection)
    {
        try
        {
            await connection.RunHandshakeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Close fires Disconnected, and the handler removes the entry.
            connection.Close(RpcDisconnectReason.HandshakeFailed);
            return;
        }

        lock (_readyLock)
        {
            _ready.Enqueue(connection);
        }
        
        connection.StartReceiving();
        _readySignal.Release();
    }

    private void RemoveAccepted(TcpServerConnection connection)
    {
        lock (_acceptedLock)
        {
            _accepted.Remove(connection);
        }
    }

    private TcpListener EnsureListener()
    {
        lock (_listenerLock)
        {
            _listener ??= new TcpListener(IPAddress.Any, _tcpOptions.Port);
            if (!_listenerStarted && !_closed)
            {
                _listener.Start();
                _listenerStarted = true;
            }

            return _listener;
        }
    }
}
