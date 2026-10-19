using NsdRpc;

namespace NsdRpc.Tests;

// One end of a deterministic in-memory pair. TrySend records a copy in
// SentFrames and delivers another into the peer's inbox. Tests pump frames
// explicitly. No threads, no timing.
internal sealed class InMemoryConnection : IRpcConnection
{
    private readonly object _inboxLock = new();
    private readonly Queue<byte[]> _inbox = new();
    private Action<RpcDisconnectReason>? _disconnectedHandlers;
    private RpcFrameHandler? _frameReceivedHandlers;
    private bool _disconnected;

    public InMemoryConnection? Peer { get; set; }

    public List<byte[]> SentFrames { get; } = [];

    internal event Action? FrameDelivered;

    public bool IsConnectedValue { get; set; } = true;

    public bool RejectSends { get; set; }

    public bool IsConnected => IsConnectedValue;

    public void Close(RpcDisconnectReason reason = RpcDisconnectReason.LocalClose)
    {
        Disconnect(reason);
    }

    public event Action<RpcDisconnectReason> Disconnected
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
        if (!IsConnectedValue || RejectSends)
        {
            return false;
        }

        byte[] copy = frame.ToArray();
        SentFrames.Add(copy);
        if (Peer == null)
        {
            return true;
        }

        lock (Peer._inboxLock)
        {
            Peer._inbox.Enqueue(copy);
        }

        Peer.FrameDelivered?.Invoke();
        return true;
    }

    public bool TryDequeueInbound(out byte[] frame)
    {
        lock (_inboxLock)
        {
            if (_inbox.Count == 0)
            {
                frame = [];
                return false;
            }

            frame = _inbox.Dequeue();
            return true;
        }
    }

    public void Disconnect(RpcDisconnectReason reason = RpcDisconnectReason.LocalClose)
    {
        if (_disconnected)
        {
            return;
        }

        _disconnected = true;
        IsConnectedValue = false;
        _disconnectedHandlers?.Invoke(reason);
        _disconnectedHandlers = null;
    }

    public static (InMemoryConnection Left, InMemoryConnection Right) CreatePair()
    {
        var left = new InMemoryConnection();
        var right = new InMemoryConnection();
        left.Peer = right;
        right.Peer = left;
        return (left, right);
    }
}
