namespace NsdRpc;

internal class PendingCall<TResponse> : PendingCallBase
    where TResponse : struct
{
    private readonly IPayloadSerializer<TResponse> _resultSerializer;

    public PendingCall(
        ulong id,
        long deadlineNanos,
        long timeoutNanos,
        IPayloadSerializer<TResponse> resultSerializer)
        : base(id, deadlineNanos, timeoutNanos)
    {
        _resultSerializer = resultSerializer;
    }

    public TaskCompletionSource<RpcResult<TResponse>> Completion { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override void CompleteResult(byte[]? payload)
    {
        if (payload == null)
        {
            Completion.TrySetResult(RpcResult<TResponse>.Success(default));
            return;
        }

        if (_resultSerializer.TryDeserialize(payload, out TResponse value))
        {
            Completion.TrySetResult(RpcResult<TResponse>.Success(value));
        }
        else
        {
            Fail((int)RpcErrorCode.InvalidRequest, "malformed response payload");
        }
    }

    public override void Fail(int errorCode, string errorMessage)
    {
        Completion.TrySetResult(RpcResult<TResponse>.Failure(errorCode, errorMessage));
    }
}
