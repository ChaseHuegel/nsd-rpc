namespace NsdRpc.Benchmarks;

// A connection that accepts every frame and drops it. It keeps a peer alive
// with pending calls that never complete.
internal sealed class SinkConnection : IRpcConnection
{
    public bool IsConnected => true;

    public void Close(RpcDisconnectReason reason = RpcDisconnectReason.LocalClose)
    {
    }

    public event Action<RpcDisconnectReason> Disconnected
    {
        add { }
        remove { }
    }

    public event RpcFrameHandler FrameReceived
    {
        add { }
        remove { }
    }

    public bool TrySend(ReadOnlySpan<byte> frame)
    {
        return true;
    }
}
