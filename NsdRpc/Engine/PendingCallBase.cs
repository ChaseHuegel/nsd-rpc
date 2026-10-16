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

    // The call consumed the chunk. NotAStream and Failed mean the call did not
    // take it. Failed also means the call is already complete.
    public virtual ChunkResult ReceiveChunk(byte[]? payload)
    {
        return ChunkResult.NotAStream;
    }

    public void DisposeRegistration()
    {
        Registration.Dispose();
    }
}
