using BenchmarkDotNet.Attributes;
using NsdRpc.Wire;

namespace NsdRpc.Benchmarks;

// A poll over a full pending table where no deadline has passed. The table
// holds MaxPendingCalls entries.
[MemoryDiagnoser]
public class TimeoutScanBenchmarks
{
    private RpcPeer _peer = null!;

    [GlobalSetup]
    public void Setup()
    {
        var options = new RpcOptions
        {
            TimestampNanos = static () => 0L,
        };
        _peer = new RpcPeer(new SinkConnection(), options);
        for (int index = 0; index < options.MaxPendingCalls; index++)
        {
            _ = _peer.InvokeAsync<Handshake, Handshake>(
                BenchmarkMethods.EchoHandshake, new Handshake(index));
        }
    }

    [Benchmark]
    public void CheckTimeoutsBelowEveryDeadline()
    {
        _peer.CheckTimeouts(0L);
    }
}
