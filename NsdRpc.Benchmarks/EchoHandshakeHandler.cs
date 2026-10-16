using NsdRpc.Wire;

namespace NsdRpc.Benchmarks;

// The simplest handler. It echoes the request. The engine path around it is
// the measured work.
internal sealed class EchoHandshakeHandler : IRpcHandler<Handshake, Handshake>
{
    public ValueTask<RpcResult<Handshake>> ExecuteAsync(
        Handshake request,
        RpcContext context,
        CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(RpcResult<Handshake>.Success(request));
    }
}
