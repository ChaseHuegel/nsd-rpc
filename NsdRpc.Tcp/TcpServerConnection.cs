using System.Net;
using System.Net.Sockets;
using NsdRpc;

namespace NsdRpc.Tcp;

/// <summary>
/// A server TCP connection. The server transport creates it at accept.
/// </summary>
public sealed class TcpServerConnection : IServerConnection, IOwnedFrameSink
{
    private readonly TcpConnection _inner;

    internal TcpServerConnection(
        Socket socket, RpcOptions rpcOptions, TcpOptions tcpOptions, ulong clientId, EndPoint? remoteEndPoint)
    {
        _inner = new TcpConnection(socket, rpcOptions, tcpOptions);
        ClientId = clientId;
        RemoteEndPoint = remoteEndPoint;
    }

    public ulong ClientId { get; }

    public EndPoint? RemoteEndPoint { get; }

    public bool IsConnected => _inner.IsConnected;

    public event Action Disconnected
    {
        add => _inner.Disconnected += value;
        remove => _inner.Disconnected -= value;
    }

    public event RpcFrameHandler FrameReceived
    {
        add => _inner.FrameReceived += value;
        remove => _inner.FrameReceived -= value;
    }

    public bool TrySend(ReadOnlySpan<byte> frame)
    {
        return _inner.TrySend(frame);
    }

    internal Task RunHandshakeAsync()
    {
        return _inner.RunHandshakeAsync();
    }

    internal void StartReceiving()
    {
        _inner.StartReceiving();
    }

    public void Close()
    {
        _inner.Close();
    }

    bool IOwnedFrameSink.TrySendOwned(byte[] frame, int length)
    {
        return _inner.TrySendOwned(frame, length);
    }
}
