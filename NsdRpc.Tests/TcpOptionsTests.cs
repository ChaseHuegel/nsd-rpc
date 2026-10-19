using NsdRpc;
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
        Assert.Equal(0, options.SendBacklogDisconnectBytes);
        Assert.Equal(0, options.SendBacklogDisconnectMs);
    }

    [Fact]
    public void BacklogThresholdAboveTheQueueCapFailsFast()
    {
        var tcpOptions = new TcpOptions
        {
            SendQueueBytes = 1024,
            SendBacklogDisconnectBytes = 1025,
            SendBacklogDisconnectMs = 100,
        };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TcpClientConnection(new RpcOptions(), tcpOptions));
    }

    [Fact]
    public void HalfConfiguredBacklogFailsFast()
    {
        var tcpOptions = new TcpOptions
        {
            SendBacklogDisconnectBytes = 1024,
            SendBacklogDisconnectMs = 0,
        };

        Assert.Throws<ArgumentException>(
            () => new TcpServerTransport(new RpcOptions(), tcpOptions));
    }
}
