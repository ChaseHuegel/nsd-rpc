namespace NsdRpc.Benchmarks;

// One end of a synchronous in-memory pair. TrySend delivers the frame to the
// peer inline. There is no socket and no copy, so the engine path is the
// measured work.
internal sealed class InMemoryConnection : IRpcConnection
{
    private Action<RpcDisconnectReason>? _disconnectedHandlers;
    private RpcFrameHandler? _frameReceivedHandlers;

    public InMemoryConnection? Peer { get; set; }

    public bool IsConnected => true;

    public void Close(RpcDisconnectReason reason = RpcDisconnectReason.LocalClose)
    {
        _disconnectedHandlers?.Invoke(reason);
        _disconnectedHandlers = null;
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
        Peer?._frameReceivedHandlers?.Invoke(frame);
        return true;
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
