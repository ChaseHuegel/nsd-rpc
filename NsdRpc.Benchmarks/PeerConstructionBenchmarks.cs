using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;

namespace NsdRpc.Benchmarks;

// The cost of one peer. The connection object is part of the measurement and
// is small. The scratch array from RpcOptions.MaxPendingCalls dominates it.
[MemoryDiagnoser]
public class PeerConstructionBenchmarks
{
    [Benchmark]
    [SuppressMessage(
        "Performance",
        "CA1822",
        Justification = "BenchmarkDotNet requires instance methods.")]
    public RpcPeer ConstructDefaultPeer()
    {
        return new RpcPeer(new InMemoryConnection());
    }
}
