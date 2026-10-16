using BenchmarkDotNet.Attributes;
using NsdRpc.Wire;

namespace NsdRpc.Benchmarks;

// The envelope wire codec. The small case isolates the fixed cost. The
// maximum case isolates the payload copy at the 65,535-byte format cap.
[MemoryDiagnoser]
public class EnvelopeCodecBenchmarks
{
    private const int SmallPayloadBytes = 64;
    private const int MaximumPayloadBytes = 65535;

    private byte[] _smallFrame = [];
    private byte[] _maximumFrame = [];
    private byte[] _serializeBuffer = [];
    private Envelope _small;
    private Envelope _maximum;

    [GlobalSetup]
    public void Setup()
    {
        _small = CreateEnvelope(SmallPayloadBytes);
        _maximum = CreateEnvelope(MaximumPayloadBytes);
        _smallFrame = _small.Serialize();
        _maximumFrame = _maximum.Serialize();
        _serializeBuffer = new byte[_maximumFrame.Length];
    }

    [Benchmark]
    public int SerializeSmallPayload()
    {
        return _small.SerializeInto(_serializeBuffer);
    }

    [Benchmark]
    public int SerializeMaximumPayload()
    {
        return _maximum.SerializeInto(_serializeBuffer);
    }

    [Benchmark]
    public Envelope DeserializeSmallPayload()
    {
        return Envelope.Deserialize(_smallFrame);
    }

    [Benchmark]
    public Envelope DeserializeMaximumPayload()
    {
        return Envelope.Deserialize(_maximumFrame);
    }

    private static Envelope CreateEnvelope(int payloadBytes)
    {
        return new Envelope(
            1,
            (byte)RpcMessageKind.Request,
            1UL,
            BenchmarkMethods.EchoHandshake,
            new byte[payloadBytes],
            null);
    }
}
