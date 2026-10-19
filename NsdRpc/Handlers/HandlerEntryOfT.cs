namespace NsdRpc;

internal sealed class HandlerEntry<TRequest, TResponse> : HandlerEntry
    where TRequest : struct
    where TResponse : struct
{
    private readonly IRpcHandler<TRequest, TResponse> _handler;
    private readonly IPayloadSerializer<TRequest> _requestSerializer;
    private readonly RpcDirection _direction;

    public HandlerEntry(
        IRpcHandler<TRequest, TResponse> handler,
        IPayloadSerializer<TRequest> requestSerializer,
        RpcDirection direction)
    {
        _handler = handler;
        _requestSerializer = requestSerializer;
        _direction = direction;
    }

    public override RpcDirection Direction => _direction;

    public override async ValueTask ExecuteAsync(
        RpcContext context, byte[]? payload, CancellationToken cancellationToken, RpcPeer peer)
    {
        TRequest request = default;
        if (payload != null && !_requestSerializer.TryDeserialize(payload, out request))
        {
            if (context.RequestId == 0UL)
            {
                RpcLog.Drop(peer.LogSink, "notification payload did not decode");
                return;
            }

            peer.TryFail(context.RequestId, (int)RpcErrorCode.InvalidParams, "invalid request payload");
            return;
        }

        RpcResult<TResponse> result = await _handler
            .ExecuteAsync(request, context, cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess && result.ErrorCode > RpcErrorCodes.AppCodeMin)
        {
            if (RpcLog.IsErrorEnabled(peer.LogSink))
            {
                RpcLog.Error(peer.LogSink, 
                    "handler used a reserved error code " + result.ErrorCode,
                    new InvalidOperationException("reserved error code"));
            }

            peer.TryFail(context.RequestId, (int)RpcErrorCode.InternalError, "internal error");
            return;
        }

        peer.TryRespond(context.RequestId, in result);
    }
}
