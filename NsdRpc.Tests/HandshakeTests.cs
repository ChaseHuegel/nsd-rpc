using NsdRpc.Wire;

namespace NsdRpc.Tests;

public sealed class HandshakeTests
{
    public static TheoryData<int> BoundaryValues => [int.MinValue, -1, 0, 1, int.MaxValue];

    [Theory]
    [MemberData(nameof(BoundaryValues))]
    public void HandshakeRoundTripsBoundaryValues(int protocolVersion)
    {
        var expected = new Handshake(protocolVersion);

        byte[] bytes = expected.Serialize();

        Handshake actual = Handshake.Deserialize(bytes, 0, bytes.Length);
        Assert.Equal(expected.ProtocolVersion, actual.ProtocolVersion);
    }

    [Fact]
    public void HandshakeRoundTripsThroughSpans()
    {
        var expected = new Handshake(int.MaxValue);

        Span<byte> buffer = stackalloc byte[expected.GetSize()];

        int written = expected.SerializeInto(buffer);
        Handshake actual = Handshake.Deserialize(buffer);

        Assert.Equal(buffer.Length, written);
        Assert.Equal(expected.ProtocolVersion, actual.ProtocolVersion);
    }

    [Fact]
    public void HandshakeRoundTripsAtOffsetWithinLargerBuffer()
    {
        var expected = new Handshake(-1);
        byte[] buffer = new byte[expected.GetSize() + 8];
        const int offset = 5;

        int written = expected.SerializeInto(buffer, offset);
        Handshake actual = Handshake.Deserialize(buffer, offset, written);

        Assert.Equal(expected.GetSize(), written);
        Assert.Equal(expected.ProtocolVersion, actual.ProtocolVersion);
    }

    [Fact]
    public void HandshakeSerializeIsDeterministic()
    {
        var handshake = new Handshake(42);

        byte[] first = handshake.Serialize();
        byte[] second = handshake.Serialize();

        Assert.Equal(first, second);
    }

    [Fact]
    public void HandshakeGetSizePinsWireLayout()
    {
        var handshake = new Handshake(42);

        // The size pins the wire layout: ushort field header plus int value.
        Assert.Equal(6, handshake.GetSize());
        Assert.Equal(handshake.GetSize(), handshake.Serialize().Length);
    }

    [Fact]
    public void HandshakeSpanRoundTripAllocatesNothing()
    {
        var handshake = new Handshake(int.MaxValue);

        // Warm up so first-call JIT work does not count.
        Span<byte> warmup = stackalloc byte[handshake.GetSize()];
        handshake.SerializeInto(warmup);
        Handshake.Deserialize(warmup);

        Span<byte> buffer = stackalloc byte[handshake.GetSize()];

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        int written = handshake.SerializeInto(buffer);
        Handshake actual = Handshake.Deserialize(buffer);
        long allocatedAfter = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(buffer.Length, written);
        Assert.Equal(handshake.ProtocolVersion, actual.ProtocolVersion);
        Assert.Equal(allocatedBefore, allocatedAfter);
    }

    [Fact]
    public void HandshakeUnpackFromEmptyBufferConsumesNothing()
    {
        Handshake actual = default;
        byte[] buffer = [];

        int consumed = actual.Unpack(buffer, 0, 0);

        Assert.Equal(0, consumed);
        Assert.Equal(0, actual.ProtocolVersion);
    }
}
