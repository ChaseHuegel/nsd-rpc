using NsdRpc;
using NsdRpc.Wire;

namespace NsdRpc.Tests;

public sealed class EnvelopeTests
{
    public static TheoryData<int> KindValues => [0, 1, 2, 3, 4, 255];

    public static TheoryData<ulong, ulong> BoundaryIdPairs => new()
    {
        { 0UL, 0UL },
        { 1UL, 1UL },
        { ulong.MaxValue, ulong.MaxValue },
        { 0UL, ulong.MaxValue },
        { ulong.MaxValue, 0UL },
    };

    [Theory]
    [MemberData(nameof(KindValues))]
    public void EnvelopeRoundTripsEveryKind(int kind)
    {
        byte[] payload = [1, 2, 3];
        var error = new RpcError((int)RpcErrorCode.InternalError, "internal");
        var expected = new Envelope(1, (byte)kind, 42UL, 7UL, payload, error);

        byte[] bytes = expected.Serialize();

        Envelope actual = Envelope.Deserialize(bytes, 0, bytes.Length);
        Assert.Equal(expected.Version, actual.Version);
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Method, actual.Method);
        Assert.Equal(payload, actual.Payload);
        Assert.NotNull(actual.Error);
        Assert.Equal(error.Code, actual.Error.Value.Code);
        Assert.Equal(error.Message, actual.Error.Value.Message);
    }

    [Theory]
    [MemberData(nameof(BoundaryIdPairs))]
    public void EnvelopeRoundTripsBoundaryIds(ulong id, ulong method)
    {
        var expected = new Envelope(1, (byte)RpcMessageKind.Request, id, method, null, null);

        byte[] bytes = expected.Serialize();

        Envelope actual = Envelope.Deserialize(bytes, 0, bytes.Length);
        Assert.Equal(id, actual.Id);
        Assert.Equal(method, actual.Method);
    }

    [Fact]
    public void EnvelopeMinimumSizeIsTwentySixBytes()
    {
        var envelope = new Envelope(1, (byte)RpcMessageKind.Request, 1UL, 1UL, null, null);

        // Four required fields: three bytes each for Version and Kind, ten
        // bytes each for Id and Method. Optional fields cost nothing when null.
        Assert.Equal(26, envelope.GetSize());
        Assert.Equal(26, envelope.Serialize().Length);
    }

    [Fact]
    public void EnvelopeMaximumSizePinsWireCap()
    {
        var payload = new byte[65535];
        var envelope = new Envelope(1, (byte)RpcMessageKind.Request, 1UL, 1UL, payload, null);

        // The nsd array cap bounds the payload at 65,535 bytes. The payload
        // field adds a 2-byte id, a 1-byte present flag, and a 2-byte count.
        Assert.Equal(65566, envelope.GetSize());
    }

    [Fact]
    public void EnvelopeSerializeIsDeterministic()
    {
        var envelope = new Envelope(
            1, (byte)RpcMessageKind.Response, 9UL, 0UL, [5, 6, 7], new RpcError(-32004, "busy"));

        byte[] first = envelope.Serialize();
        byte[] second = envelope.Serialize();

        Assert.Equal(first, second);
    }

    [Fact]
    public void EnvelopeSpanRoundTripAllocatesNothing()
    {
        var envelope = new Envelope(1, (byte)RpcMessageKind.Response, 5UL, 0UL, null, null);

        // Warm up so first-call JIT work does not count.
        Span<byte> warmup = stackalloc byte[envelope.GetSize()];
        envelope.SerializeInto(warmup);
        Envelope.Deserialize(warmup);

        Span<byte> buffer = stackalloc byte[envelope.GetSize()];

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        int written = envelope.SerializeInto(buffer);
        Envelope actual = Envelope.Deserialize(buffer);
        long allocatedAfter = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(envelope.GetSize(), written);
        Assert.Equal(envelope.Id, actual.Id);
        Assert.Null(actual.Payload);
        Assert.Null(actual.Error);
        Assert.Equal(allocatedBefore, allocatedAfter);
    }

    [Fact]
    public void EnvelopeIgnoresAnAppendedFieldWhoseValueScansAsAKnownId()
    {
        var envelope = new Envelope(
            1, (byte)RpcMessageKind.Request, 9UL, 3UL, [7], new RpcError(-32600, "bad"));
        byte[] bytes = envelope.Serialize();

        // Append field id 6 with a value whose first two bytes scan as field
        // id 0. The reader has read every known field, so it stops.
        byte[] withUnknown = new byte[bytes.Length + 6];
        bytes.CopyTo(withUnknown, 0);
        withUnknown[bytes.Length] = 6;
        withUnknown[bytes.Length + 2] = 0;
        withUnknown[bytes.Length + 3] = 0;
        withUnknown[bytes.Length + 4] = 0;
        withUnknown[bytes.Length + 5] = 0;

        Envelope actual = Envelope.Deserialize(withUnknown);

        Assert.Equal(envelope.Version, actual.Version);
        Assert.Equal(envelope.Kind, actual.Kind);
        Assert.Equal(9UL, actual.Id);
        Assert.Equal(3UL, actual.Method);
        Assert.Equal(new byte[] { 7 }, actual.Payload);
        Assert.Equal(-32600, actual.Error!.Value.Code);
        Assert.Equal("bad", actual.Error.Value.Message);
    }

    [Fact]
    public void EnvelopeIgnoresTrailingUnknownFields()
    {
        var envelope = new Envelope(1, (byte)RpcMessageKind.Request, 9UL, 3UL, [7], null);
        byte[] bytes = envelope.Serialize();

        // Append an unknown field: id 9 and a value. The reader scans trailing
        // bytes pairwise. The known fields must survive the scan.
        byte[] withUnknown = new byte[bytes.Length + 6];
        bytes.CopyTo(withUnknown, 0);
        withUnknown[bytes.Length] = 9;
        withUnknown[bytes.Length + 2] = 4;
        withUnknown[bytes.Length + 3] = 3;
        withUnknown[bytes.Length + 4] = 2;
        withUnknown[bytes.Length + 5] = 1;

        Envelope actual = Envelope.Deserialize(withUnknown);

        Assert.Equal(envelope.Version, actual.Version);
        Assert.Equal(envelope.Kind, actual.Kind);
        Assert.Equal(9UL, actual.Id);
        Assert.Equal(3UL, actual.Method);
        Assert.Equal(new byte[] { 7 }, actual.Payload);
    }

    [Fact]
    public void EnvelopeMinimalEveryTruncationIsSafe()
    {
        var sample = new Envelope(1, (byte)RpcMessageKind.Request, 1UL, 1UL, null, null);

        TruncatedBufferHelper.AssertEveryTruncationIsSafe(
            in sample,
            (in Envelope v) => v.Serialize(),
            (ref Envelope v, ReadOnlySpan<byte> d) => v.Unpack(d));
    }

    [Fact]
    public void EnvelopeFullEveryTruncationIsSafe()
    {
        var sample = new Envelope(
            1,
            (byte)RpcMessageKind.Response,
            42UL,
            7UL,
            [1, 2, 3, 4],
            new RpcError((int)RpcErrorCode.InternalError, "boom"));

        TruncatedBufferHelper.AssertEveryTruncationIsSafe(
            in sample,
            (in Envelope v) => v.Serialize(),
            (ref Envelope v, ReadOnlySpan<byte> d) => v.Unpack(d));
    }
}
