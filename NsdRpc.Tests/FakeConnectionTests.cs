using NsdRpc;

namespace NsdRpc.Tests;

public sealed class FakeConnectionTests
{
    [Fact]
    public void FakeConnectionCopiesFramesBeforeReturning()
    {
        var connection = new FakeConnection();
        byte[] source = [1, 2, 3, 4];

        bool sent = connection.TrySend(source);

        source[0] = 99;

        Assert.True(sent);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, Assert.Single(connection.SentFrames));
    }

    [Fact]
    public void FakeConnectionRejectsSendsWhenClosed()
    {
        var connection = new FakeConnection();
        connection.FireDisconnected();

        bool sent = connection.TrySend([1]);

        Assert.False(sent);
        Assert.Empty(connection.SentFrames);
    }

    [Fact]
    public void FakeConnectionFiresDisconnectedExactlyOnce()
    {
        var connection = new FakeConnection();
        int fired = 0;
        connection.Disconnected += () => fired++;

        connection.FireDisconnected();
        connection.FireDisconnected();

        Assert.Equal(1, fired);
        Assert.False(connection.IsConnected);
    }

    [Fact]
    public async Task FakeServerTransportStopsAcceptingAfterClose()
    {
        var transport = new FakeServerTransport();
        transport.Enqueue(new FakeServerConnection(1));

        IServerConnection? first = await transport.AcceptAsync();
        transport.Close();
        IServerConnection? second = await transport.AcceptAsync();

        Assert.NotNull(first);
        Assert.Equal(1UL, first.ClientId);
        Assert.Null(second);
        Assert.Equal(1, transport.AcceptCount);
    }
}
