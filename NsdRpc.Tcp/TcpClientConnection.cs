using System.Buffers;
using System.Net.Sockets;
using NsdRpc;

namespace NsdRpc.Tcp;

/// <summary>
/// A client TCP connection. Connect, then use it with RpcClient.
/// </summary>
public sealed class TcpClientConnection : IClientConnection, IOwnedFrameSink
{
    private readonly RpcOptions _rpcOptions;
    private readonly TcpOptions _tcpOptions;
    private TcpConnection? _connection;
    private Action? _disconnectedHandlers;
    private RpcFrameHandler? _frameReceivedHandlers;

    public TcpClientConnection(RpcOptions rpcOptions, TcpOptions tcpOptions)
    {
        _rpcOptions = rpcOptions ?? throw new ArgumentNullException(nameof(rpcOptions));
        _tcpOptions = tcpOptions ?? throw new ArgumentNullException(nameof(tcpOptions));
    }

    public bool IsConnected => _connection?.IsConnected ?? false;

    public void Close()
    {
        _connection?.Close();
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

    public event Action Disconnected
    {
        add => _disconnectedHandlers += value;
        remove => _disconnectedHandlers -= value;
    }

    public event RpcFrameHandler FrameReceived
    {
        add => _frameReceivedHandlers += value;
        remove => _frameReceivedHandlers -= value;
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
        await socket.ConnectAsync(_tcpOptions.Host, _tcpOptions.Port).ConfigureAwait(false);

        var connection = new TcpConnection(socket, _rpcOptions, _tcpOptions);
        connection.Disconnected += ForwardDisconnected;
        connection.FrameReceived += ForwardFrame;
        try
        {
            await connection.RunHandshakeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            connection.Close();
            throw;
        }

        _connection = connection;
        connection.StartReceiving();
    }

    private void ForwardDisconnected()
    {
        _disconnectedHandlers?.Invoke();
    }

    private void ForwardFrame(ReadOnlySpan<byte> frame)
    {
        _frameReceivedHandlers?.Invoke(frame);
    }
}
