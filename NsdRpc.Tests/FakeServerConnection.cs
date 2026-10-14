using System.Net;
using NsdRpc;

namespace NsdRpc.Tests;

internal sealed class FakeServerConnection : IServerConnection
{
    private readonly FakeConnection _inner = new();

    public FakeServerConnection(ulong clientId)
    {
        ClientId = clientId;
    }

    public ulong ClientId { get; }

    public EndPoint? RemoteEndPoint => null;

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

    public List<byte[]> SentFrames => _inner.SentFrames;

    public void FireDisconnected()
    {
        _inner.FireDisconnected();
    }
}
