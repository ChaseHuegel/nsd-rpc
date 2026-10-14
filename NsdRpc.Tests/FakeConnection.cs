using NsdRpc;

namespace NsdRpc.Tests;

// A recording connection. Tests drive it. It implements the full IRpcConnection
// contract: copies frames, rejects sends when closed, fires Disconnected once.
internal sealed class FakeConnection : IRpcConnection
{
    public readonly List<byte[]> SentFrames = [];

    private Action? _disconnectedHandlers;
    private bool _disconnected;

    public bool IsConnectedValue { get; set; } = true;

    public bool IsConnected => IsConnectedValue;

    public event Action Disconnected
    {
        add => _disconnectedHandlers += value;
        remove => _disconnectedHandlers -= value;
    }

    public bool TrySend(ReadOnlySpan<byte> frame)
    {
        if (!IsConnectedValue)
        {
            return false;
        }

        SentFrames.Add(frame.ToArray());
        return true;
    }

    public void FireDisconnected()
    {
        if (_disconnected)
        {
            return;
        }

        _disconnected = true;
        IsConnectedValue = false;
        _disconnectedHandlers?.Invoke();
        _disconnectedHandlers = null;
    }
}
