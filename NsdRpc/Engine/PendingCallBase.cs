namespace NsdRpc;

// One outbound call awaiting a response. The peer owns the table.
internal abstract class PendingCallBase
{
    protected PendingCallBase(ulong id, long deadlineNanos, long timeoutNanos)
    {
        Id = id;
        DeadlineNanos = deadlineNanos;
        TimeoutNanos = timeoutNanos;
    }

    public readonly ulong Id;

    public long DeadlineNanos;

    public readonly long TimeoutNanos;

    public CancellationTokenRegistration Registration;

    public abstract void CompleteResult(byte[]? payload);

    public abstract void Fail(int errorCode, string errorMessage);

    // True when the call consumed the chunk. False means the call has no stream.
    public virtual bool ReceiveChunk(byte[]? payload)
    {
        return false;
    }

    public void DisposeRegistration()
    {
        Registration.Dispose();
    }
}
