using NsdRpc;
using NsdRpc.Wire;

namespace NsdRpc.Tests;

public sealed class RpcResultTests
{
    [Fact]
    public void SuccessDoesNotAllocate()
    {
        // Warm up so first-call JIT work does not count.
        _ = RpcResult<Handshake>.Success(new Handshake(1));

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        RpcResult<Handshake> result = RpcResult<Handshake>.Success(new Handshake(1));
        long allocatedAfter = GC.GetAllocatedBytesForCurrentThread();

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.ProtocolVersion);
        Assert.Equal(0, result.ErrorCode);
        Assert.Equal(string.Empty, result.ErrorMessage);
        Assert.Equal(allocatedBefore, allocatedAfter);
    }

    [Fact]
    public void FailureKeepsTheCodeAndMessage()
    {
        RpcResult<Handshake> result = RpcResult<Handshake>.Failure(-32100, "app code");

        Assert.False(result.IsSuccess);
        Assert.Equal(-32100, result.ErrorCode);
        Assert.Equal("app code", result.ErrorMessage);
    }

    [Fact]
    public void FailureRejectsZeroErrorCode()
    {
        Assert.Throws<InvalidOperationException>(() => RpcResult<Handshake>.Failure(0, "zero"));
    }
}
