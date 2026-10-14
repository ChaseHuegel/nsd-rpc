namespace NsdRpc;

// One queued dispatch. Inbound is null for a standalone notification.
internal readonly struct DispatchEntry
{
    public DispatchEntry(InboundCall? inbound, ulong method, byte[]? payload)
    {
        Inbound = inbound;
        Method = method;
        Payload = payload;
    }

    public readonly InboundCall? Inbound;

    public readonly ulong Method;

    public readonly byte[]? Payload;
}
