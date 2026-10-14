namespace NsdRpc;

/// <summary>
/// One RPC handler. Return a result, or keep the context and respond later.
/// The parameters pass by value so async handlers can implement the interface.
/// </summary>
public interface IRpcHandler<TRequest, TResponse>
    where TRequest : struct
    where TResponse : struct
{
    ValueTask<RpcResult<TResponse>> ExecuteAsync(
        TRequest request, RpcContext context, CancellationToken cancellationToken);
}
