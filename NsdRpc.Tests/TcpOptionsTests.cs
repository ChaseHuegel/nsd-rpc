using NsdRpc.Tcp;

namespace NsdRpc.Tests;

public sealed class TcpOptionsTests
{
    [Fact]
    public void TcpOptionsDefaultsMatchTheSpec()
    {
        var options = new TcpOptions();

        Assert.Equal("localhost", options.Host);
        Assert.Equal(0, options.Port);
        Assert.True(options.NoDelay);
        Assert.Equal(TimeSpan.FromSeconds(10), options.HandshakeTimeout);
        Assert.Equal(TimeSpan.Zero, options.IdleTimeout);
        Assert.Equal(65536, options.ReceiveBufferBytes);
        Assert.Equal(TimeSpan.FromSeconds(30), options.DecoderIdleShrinkAfter);
        Assert.Equal(1048576, options.SendQueueBytes);
    }
}
