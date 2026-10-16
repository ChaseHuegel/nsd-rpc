using NsdRpc;

namespace NsdRpc.Tests;

public sealed class RpcOptionsTests
{
    [Fact]
    public void RpcOptionsDefaultsMatchTheSpec()
    {
        var options = new RpcOptions();

        Assert.Equal(TimeSpan.FromMilliseconds(30000), options.Timeout);
        Assert.Equal(65570, options.MaxFrameBytes);
        Assert.Equal(1024, options.MaxPendingCalls);
        Assert.Equal(1024, options.MaxBufferedChunks);
        Assert.NotNull(options.TimestampNanos);
    }

    [Fact]
    public void RpcOptionsDefaultClockIsMonotonic()
    {
        var options = new RpcOptions();

        long first = options.TimestampNanos();
        long second = options.TimestampNanos();

        Assert.True(second >= first);
    }
}
