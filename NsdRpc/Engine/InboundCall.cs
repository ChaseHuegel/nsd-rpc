namespace NsdRpc;

// One inbound request awaiting a handler response. The peer tracks it until
// the handler responds or the connection dies.
internal sealed class InboundCall
{
    public InboundCall(ulong requestId, ulong method)
    {
        RequestId = requestId;
        Method = method;
    }

    public readonly ulong RequestId;

    public readonly ulong Method;

    public bool Cancelled;

    public CancellationTokenSource? CancelSource;
}
