using NsdRpc;
using NsdRpc.Wire;

namespace NsdRpc.Tests;

public sealed class RpcErrorTests
{
    public static TheoryData<int, string> ErrorValues => new()
    {
        { int.MinValue, "" },
        { (int)RpcErrorCode.InvalidRequest, "invalid" },
        { (int)RpcErrorCode.Cancelled, new string('x', 300) },
        { int.MaxValue, "mixed case and spaces" },
    };

    [Theory]
    [MemberData(nameof(ErrorValues))]
    public void RpcErrorRoundTrips(int code, string message)
    {
        var expected = new RpcError(code, message);

        byte[] bytes = expected.Serialize();

        RpcError actual = RpcError.Deserialize(bytes, 0, bytes.Length);
        Assert.Equal(code, actual.Code);
        Assert.Equal(message, actual.Message);
    }

    [Fact]
    public void RpcErrorPinsMinimumWireSize()
    {
        var error = new RpcError(0, "");

        // Code: 2-byte field id plus 4-byte int. Message: 2-byte field id plus
        // 2-byte length with no chars.
        Assert.Equal(10, error.GetSize());
        Assert.Equal(10, error.Serialize().Length);
    }

    [Fact]
    public void RpcErrorSerializeIsDeterministic()
    {
        var error = new RpcError((int)RpcErrorCode.Busy, "busy");

        byte[] first = error.Serialize();
        byte[] second = error.Serialize();

        Assert.Equal(first, second);
    }

    [Fact]
    public void RpcErrorEveryTruncationIsSafe()
    {
        var sample = new RpcError((int)RpcErrorCode.InternalError, "a message to truncate");

        TruncatedBufferHelper.AssertEveryTruncationIsSafe(
            in sample,
            (in RpcError v) => v.Serialize(),
            (ref RpcError v, ReadOnlySpan<byte> d) => v.Unpack(d));
    }
}
