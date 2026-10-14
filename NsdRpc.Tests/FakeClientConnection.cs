using NsdRpc;

namespace NsdRpc.Tests;

internal sealed class FakeClientConnection : IClientConnection
{
    private readonly FakeConnection _inner = new();

    public int ConnectCount { get; private set; }

    public bool IsConnected => _inner.IsConnected;

    public event Action Disconnected
    {
        add => _inner.Disconnected += value;
        remove => _inner.Disconnected -= value;
    }

    public bool TrySend(ReadOnlySpan<byte> frame)
    {
        return _inner.TrySend(frame);
    }

    public Task ConnectAsync()
    {
        ConnectCount++;
        return Task.CompletedTask;
    }

    public List<byte[]> SentFrames => _inner.SentFrames;

    public void FireDisconnected()
    {
        _inner.FireDisconnected();
    }
}
