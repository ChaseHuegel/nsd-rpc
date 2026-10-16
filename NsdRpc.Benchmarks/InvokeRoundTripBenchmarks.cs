using BenchmarkDotNet.Attributes;
using NsdRpc.Wire;

namespace NsdRpc.Benchmarks;

// One request and one response over the engine with a synchronous in-memory
// connection. The await covers the dispatch worker and the completion path.
[MemoryDiagnoser]
public class InvokeRoundTripBenchmarks
{
    private RpcClient _caller = null!;
    private Handshake _request;

    [GlobalSetup]
    public void Setup()
    {
        (InMemoryConnection callerSide, InMemoryConnection calleeSide) = InMemoryConnection.CreatePair();
        var registry = new RpcRegistry();
        registry.Add<Handshake, Handshake>(
            new EchoHandshakeHandler(), BenchmarkMethods.EchoHandshake);
        _caller = new RpcClient(callerSide, new RpcRegistry());
        _ = new RpcClient(calleeSide, registry);
        _request = new Handshake(7);
    }

    [Benchmark]
    public async Task<RpcResult<Handshake>> InvokeRoundTrip()
    {
        return await _caller
            .InvokeAsync<Handshake, Handshake>(BenchmarkMethods.EchoHandshake, in _request)
            .ConfigureAwait(false);
    }
}
