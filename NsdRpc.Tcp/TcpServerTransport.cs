using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using NsdRpc;

namespace NsdRpc.Tcp;

/// <summary>
/// A TCP listener. Accept one connection per AcceptAsync call.
/// </summary>
public sealed class TcpServerTransport : IRpcServerTransport
{
    private readonly RpcOptions _rpcOptions;
    private readonly TcpOptions _tcpOptions;
    private readonly object _listenerLock = new();
    private readonly object _acceptedLock = new();
    private readonly List<TcpServerConnection> _accepted = [];
    private TcpListener? _listener;
    private bool _listenerStarted;
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

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "A failed handshake skips one connection and keeps accepting. It is a transport boundary.")]
    public async Task<IServerConnection?> AcceptAsync()
    {
        TcpListener listener = EnsureListener();
        while (true)
        {
            if (_closed)
            {
                return null;
            }

            Socket socket;
            try
            {
                socket = await listener.AcceptSocketAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                return null;
            }

            if (_closed)
            {
                socket.Dispose();
                return null;
            }

            socket.NoDelay = _tcpOptions.NoDelay;
            ulong clientId = checked(_nextClientId++);
            var connection = new TcpServerConnection(
                socket, _rpcOptions, _tcpOptions, clientId, socket.RemoteEndPoint);
            // A disconnect removes the connection from the list. The list stays
            // bounded by the live connection count.
            connection.Disconnected += () => RemoveAccepted(connection);
            lock (_acceptedLock)
            {
                _accepted.Add(connection);
            }

            try
            {
                await connection.RunHandshakeAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Close fires Disconnected, and the handler removes the entry.
                connection.Close();
                continue;
            }

            connection.StartReceiving();
            return connection;
        }
    }

    public void Close()
    {
        _closed = true;
        _listener?.Stop();
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
            if (!_listenerStarted)
            {
                _listener.Start();
                _listenerStarted = true;
            }

            return _listener;
        }
    }
}
