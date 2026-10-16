using NsdRpc.Wire;

namespace NsdRpc.Benchmarks;

// Sends a fixed number of chunks, then the response.
internal sealed class StreamHandshakeHandler : IRpcHandler<Handshake, Handshake>
{
    private const int ChunkCount = 10;

    public ValueTask<RpcResult<Handshake>> ExecuteAsync(
        Handshake request,
        RpcContext context,
        CancellationToken cancellationToken)
    {
        for (int index = 0; index < ChunkCount; index++)
        {
            context.Stream(new Handshake(index));
        }

        return ValueTask.FromResult(RpcResult<Handshake>.Success(request));
    }
}
