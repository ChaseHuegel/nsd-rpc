using System.Buffers.Binary;
using BenchmarkDotNet.Attributes;
using NsdRpc.Wire;

namespace NsdRpc.Benchmarks;

// One frame with an unknown kind. The peer drops it and logs the drop. The
// measurement shows the drop path, including the log message.
[MemoryDiagnoser]
public class DropPathBenchmarks
{
    private RpcPeer _peer = null!;
    private byte[] _unknownKindFrame = [];

    [GlobalSetup]
    public void Setup()
    {
        _peer = new RpcPeer(new SinkConnection());
        var envelope = new Envelope(
            1,
            9,
            1UL,
            BenchmarkMethods.EchoHandshake,
            null,
            null);
        byte[] envelopeBytes = envelope.Serialize();
        _unknownKindFrame = new byte[4 + envelopeBytes.Length];
        BinaryPrimitives.WriteInt32LittleEndian(_unknownKindFrame, envelopeBytes.Length);
        envelopeBytes.CopyTo(_unknownKindFrame, 4);
    }

    [Benchmark]
    public void HandleUnknownKind()
    {
        _peer.HandleFrame(_unknownKindFrame);
    }
}
