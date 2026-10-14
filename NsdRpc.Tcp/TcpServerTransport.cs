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
                connection.Close();
                lock (_acceptedLock)
                {
                    _accepted.Remove(connection);
                }

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
        lock (_acceptedLock)
        {
            foreach (TcpServerConnection connection in _accepted)
            {
                connection.Close();
            }

            _accepted.Clear();
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
