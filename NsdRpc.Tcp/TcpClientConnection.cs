using System.Buffers;
using System.Net.Sockets;
using NsdRpc;

namespace NsdRpc.Tcp;

/// <summary>
/// A client TCP connection. Connect, then use it with RpcClient.
/// </summary>
public sealed class TcpClientConnection : IClientConnection, IOwnedFrameSink, IRpcConnectionStats
{
    private readonly RpcOptions _rpcOptions;
    private readonly TcpOptions _tcpOptions;
    private readonly Func<Socket, Task> _connectEndpoint;
    private readonly object _eventLock = new();
    private TcpConnection? _connection;
    private Action<RpcDisconnectReason>? _disconnectedHandlers;
    private RpcFrameHandler? _frameReceivedHandlers;

    public TcpClientConnection(RpcOptions rpcOptions, TcpOptions tcpOptions)
        : this(rpcOptions, tcpOptions, null)
    {
    }

    // A test seam. The connector lets a test hold the connect open without a
    // routed address. A null connector connects the socket to the options.
    internal TcpClientConnection(
        RpcOptions rpcOptions, TcpOptions tcpOptions, Func<Socket, Task>? connectEndpoint)
    {
        _rpcOptions = rpcOptions ?? throw new ArgumentNullException(nameof(rpcOptions));
        _tcpOptions = tcpOptions ?? throw new ArgumentNullException(nameof(tcpOptions));
        TcpOptionValidation.Validate(_tcpOptions);
        _connectEndpoint = connectEndpoint ?? ConnectEndpoint;
    }

    public bool IsConnected => _connection?.IsConnected ?? false;

    public RpcConnectionStats Stats => _connection?.Stats ?? default;

    public void Close(RpcDisconnectReason reason = RpcDisconnectReason.LocalClose)
    {
        _connection?.Close(reason);
    }

    bool IOwnedFrameSink.TrySendOwned(byte[] frame, int length)
    {
        if (_connection == null)
        {
            ArrayPool<byte>.Shared.Return(frame, clearArray: true);
            return false;
        }

        return _connection.TrySendOwned(frame, length);
    }

    public event Action<RpcDisconnectReason> Disconnected
    {
        add => _disconnectedHandlers += value;
        remove => _disconnectedHandlers -= value;
    }

    public event RpcFrameHandler FrameReceived
    {
        add
        {
            if (value == null)
            {
                return;
            }

            TcpConnection? connection;
            lock (_eventLock)
            {
                connection = _connection;
                if (connection == null)
                {
                    _frameReceivedHandlers += value;
                    return;
                }
            }

            connection.FrameReceived += value;
        }
        remove
        {
            if (value == null)
            {
                return;
            }

            TcpConnection? connection;
            lock (_eventLock)
            {
                connection = _connection;
                if (connection == null)
                {
                    _frameReceivedHandlers -= value;
                    return;
                }
            }

            connection.FrameReceived -= value;
        }
    }

    public bool TrySend(ReadOnlySpan<byte> frame)
    {
        return _connection?.TrySend(frame) ?? false;
    }

    public async Task ConnectAsync()
    {
        if (_connection != null)
        {
            throw new InvalidOperationException("The connection already exists.");
        }

        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.NoDelay = _tcpOptions.NoDelay;
        try
        {
            await ConnectSocketAsync(socket).ConfigureAwait(false);
        }
        catch (Exception)
        {
            socket.Dispose();
            throw;
        }

        var connection = new TcpConnection(socket, _rpcOptions, _tcpOptions);
        connection.Disconnected += ForwardDisconnected;
        try
        {
            await connection.RunHandshakeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            connection.Close(RpcDisconnectReason.HandshakeFailed);
            throw;
        }

        lock (_eventLock)
        {
            if (_frameReceivedHandlers != null)
            {
                connection.FrameReceived += _frameReceivedHandlers;
                _frameReceivedHandlers = null;
            }

            _connection = connection;
        }

        connection.StartReceiving();
    }

    private Task ConnectEndpoint(Socket socket)
    {
        return socket.ConnectAsync(_tcpOptions.Host, _tcpOptions.Port);
    }

    private async Task ConnectSocketAsync(Socket socket)
    {
        if (_tcpOptions.ConnectTimeout <= TimeSpan.Zero)
        {
            await _connectEndpoint(socket).ConfigureAwait(false);
            return;
        }

        using var timeout = new CancellationTokenSource();
        Task connect = _connectEndpoint(socket);
        Task delay = Task.Delay(ClampTimeout(_tcpOptions.ConnectTimeout), timeout.Token);
        Task completed = await Task.WhenAny(connect, delay).ConfigureAwait(false);
        timeout.Cancel();
        if (completed != connect)
        {
            socket.Dispose();
            // The timeout owns the result. Observe the abandoned connect fault.
            _ = connect.ContinueWith(
                static task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            throw new TimeoutException(
                "The connect to " + _tcpOptions.Host + ":" + _tcpOptions.Port
                + " passed its deadline.");
        }

        await connect.ConfigureAwait(false);
    }

    // A token source rejects a delay above Int32.MaxValue milliseconds.
    private static TimeSpan ClampTimeout(TimeSpan value)
    {
        TimeSpan max = TimeSpan.FromMilliseconds(int.MaxValue);
        return value > max ? max : value;
    }

    private void ForwardDisconnected(RpcDisconnectReason reason)
    {
        _disconnectedHandlers?.Invoke(reason);
    }
}
