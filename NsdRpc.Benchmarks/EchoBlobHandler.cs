using NsdRpc.Benchmarks.Wire;

namespace NsdRpc.Benchmarks;

// Echoes a blob. The TCP benchmark uses it for a multi-kilobyte frame.
internal sealed class EchoBlobHandler : IRpcHandler<Blob, Blob>
{
    public ValueTask<RpcResult<Blob>> ExecuteAsync(
        Blob request,
        RpcContext context,
        CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(RpcResult<Blob>.Success(request));
    }
}
