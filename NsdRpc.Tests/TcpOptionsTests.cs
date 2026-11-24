using System.Net;
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
        Assert.Equal(IPAddress.Any, options.BindAddress);
        Assert.Equal(TimeSpan.FromSeconds(10), options.ConnectTimeout);
        Assert.True(options.NoDelay);
        Assert.Equal(TimeSpan.FromSeconds(10), options.HandshakeTimeout);
        Assert.Equal(TimeSpan.Zero, options.IdleTimeout);
        Assert.Equal(65536, options.ReceiveBufferBytes);
        Assert.Equal(1048576, options.ReceiveQueueBytes);
        Assert.Equal(TimeSpan.FromSeconds(30), options.DecoderIdleShrinkAfter);
        Assert.Equal(1048576, options.SendQueueBytes);
        Assert.Equal(TimeSpan.Zero, options.SendBatchInterval);
        Assert.Equal(65536, options.SendBatchMaxBytes);
        Assert.Equal(0, options.SendBacklogDisconnectBytes);
        Assert.Equal(0, options.SendBacklogDisconnectMs);
    }

    [Fact]
    public void NullBindAddressFailsFast()
    {
        var tcpOptions = new TcpOptions { BindAddress = null! };

        Assert.Throws<ArgumentNullException>(
            () => new TcpServerTransport(new RpcOptions(), tcpOptions));
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

    [Fact]
    public void NonPositiveReceiveQueueBytesFailsFast()
    {
        var zero = new TcpOptions { ReceiveQueueBytes = 0 };
        var negative = new TcpOptions { ReceiveQueueBytes = -1 };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TcpServerTransport(new RpcOptions(), zero));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TcpClientConnection(new RpcOptions(), negative));
    }
}
