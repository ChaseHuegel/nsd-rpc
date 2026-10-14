using System.IO;
using NsdRpc.Wire;

namespace NsdRpc.Tests;

public sealed class TruncatedBufferTests
{
    [Fact]
    public void HandshakeEveryTruncationIsSafe()
    {
        var sample = new Handshake(42);

        TruncatedBufferHelper.AssertEveryTruncationIsSafe(
            in sample,
            (in Handshake v) => v.Serialize(),
            (ref Handshake v, ReadOnlySpan<byte> d) => v.Unpack(d));
    }

    // Pins the exact contract per truncated length. A value of -1 means the
    // reader throws InvalidDataException. The old compiler over-read on lengths
    // 2 through 5 and reported consumed 6. That outcome fails this test.
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, -1)]
    [InlineData(3, -1)]
    [InlineData(4, -1)]
    [InlineData(5, -1)]
    public void HandshakeTruncationOutcomesAreExact(int length, int expectedConsumed)
    {
        byte[] full = new Handshake(42).Serialize();
        var value = new Handshake();

        if (expectedConsumed < 0)
        {
            Assert.Throws<InvalidDataException>(() => value.Unpack(full.AsSpan(0, length)));
            return;
        }

        int consumed = value.Unpack(full.AsSpan(0, length));
        Assert.Equal(expectedConsumed, consumed);
    }

    [Fact]
    public void HandshakeRejectsNegativeLength()
    {
        byte[] buffer = new Handshake(42).Serialize();
        var value = new Handshake();

        Assert.Throws<InvalidDataException>(() => value.Unpack(buffer, 0, -1));
    }

    [Fact]
    public void HandshakeRejectsStartBeyondBuffer()
    {
        byte[] buffer = new Handshake(42).Serialize();
        var value = new Handshake();

        Assert.Throws<InvalidDataException>(() => value.Unpack(buffer, 7, 6));
    }
}
