using BenchmarkDotNet.Attributes;
using NsdRpc.Wire;

namespace NsdRpc.Benchmarks;

// One stream request, ten chunk notifications, and one response. The caller
// drains the session through the async enumerable.
[MemoryDiagnoser]
public class StreamRoundTripBenchmarks
{
    private RpcClient _caller = null!;
    private Handshake _request;

    [GlobalSetup]
    public void Setup()
    {
        (InMemoryConnection callerSide, InMemoryConnection calleeSide) = InMemoryConnection.CreatePair();
        var registry = new RpcRegistry();
        registry.Add<Handshake, Handshake>(
            new StreamHandshakeHandler(), BenchmarkMethods.StreamHandshake);
        _caller = new RpcClient(callerSide, new RpcRegistry());
        _ = new RpcClient(calleeSide, registry);
        _request = new Handshake(7);
    }

    [Benchmark]
    public async Task<int> StreamRoundTrip()
    {
        RpcStreamSession<Handshake, Handshake> session = _caller
            .InvokeStreamAsync<Handshake, Handshake, Handshake>(
                BenchmarkMethods.StreamHandshake, in _request);

        int chunkCount = 0;
        await foreach (Handshake chunk in session.Chunks)
        {
            chunkCount++;
        }

        RpcResult<Handshake> response = await session.Response.ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            throw new InvalidOperationException("The stream response failed.");
        }

        return chunkCount;
    }
}
