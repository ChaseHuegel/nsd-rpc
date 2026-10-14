using NsdRpc.Wire;

namespace NsdRpc.Tests;

public sealed class HandshakeTests
{
    [Fact]
    public void HandshakeRoundTrips()
    {
        var expected = new Handshake(1);

        byte[] bytes = expected.Serialize();

        Handshake actual = Handshake.Deserialize(bytes, 0, bytes.Length);
        Assert.Equal(expected.ProtocolVersion, actual.ProtocolVersion);
    }
}
