using NsdRpc;
using NsdRpc.Wire;

namespace NsdRpc.Tests;

public sealed class RpcServerTests
{
    [RpcMethod(202)]
    private sealed class EchoHandler : IRpcHandler<Handshake, Handshake>
    {
        public ValueTask<RpcResult<Handshake>> ExecuteAsync(
            Handshake request, RpcContext context, CancellationToken cancellationToken)
        {
            return new ValueTask<RpcResult<Handshake>>(
                RpcResult<Handshake>.Success(new Handshake(request.ProtocolVersion + 1)));
        }
    }

    private static (
        RpcServer Server,
        InMemoryConnection ClientConn,
        InMemoryConnection Inner,
        RpcClient Client,
        TaskCompletionSource AcceptGate)
        StartServer(ulong clientId, RpcOptions? options = null)
    {
        var registry = new RpcRegistry();
        registry.Add(new EchoHandler());
        (InMemoryConnection clientConn, InMemoryConnection inner) = InMemoryConnection.CreatePair();
        var serverConn = new InMemoryServerConnection(inner, clientId);
        var transport = new FakeServerTransport();
        transport.Enqueue(serverConn);
        var server = new RpcServer(transport, registry, options);
        var acceptGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.ClientConnected += peer => acceptGate.TrySetResult();
        server.Start();
        var client = new RpcClient(clientConn, new RpcRegistry());
        return (server, clientConn, inner, client, acceptGate);
    }

    [Fact]
    public async Task ServerAcceptsAndServesClientCalls()
    {
        (RpcServer server, InMemoryConnection clientConn, InMemoryConnection inner, RpcClient client,
            TaskCompletionSource acceptGate) = StartServer(7);

        await acceptGate.Task;
        RpcPeer serverPeer = server.GetPeer(7)!;
        Assert.True(serverPeer.IsServer);

        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        clientConn.FrameDelivered += () => delivered.TrySetResult();
        Task<RpcResult<Handshake>> call = client.InvokeAsync<Handshake, Handshake>(202, new Handshake(41));
        RpcTestHelpers.Pump(inner, serverPeer);
        await delivered.Task;
        RpcTestHelpers.Pump(clientConn, client);

        RpcResult<Handshake> result = await call;
        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value.ProtocolVersion);
    }

    [Fact]
    public async Task ServerRaisesClientConnectedAndDisconnected()
    {
        var registry = new RpcRegistry();
        (InMemoryConnection clientConn, InMemoryConnection inner) = InMemoryConnection.CreatePair();
        var transport = new FakeServerTransport();
        transport.Enqueue(new InMemoryServerConnection(inner, 7));
        var server = new RpcServer(transport, registry);

        RpcPeer? connected = null;
        RpcPeer? disconnected = null;
        var connectedGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnectedGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.ClientConnected += peer =>
        {
            connected = peer;
            connectedGate.TrySetResult();
        };
        server.ClientDisconnected += peer =>
        {
            disconnected = peer;
            disconnectedGate.TrySetResult();
        };

        server.Start();
        await connectedGate.Task;
        Assert.NotNull(connected);
        Assert.Equal(7UL, connected.ClientId);

        inner.Disconnect();
        await disconnectedGate.Task;
        Assert.Same(connected, disconnected);
        Assert.Null(server.GetPeer(7));
    }

    [Fact]
    public async Task ServerRaisesClientDisconnectedOnPruneAndClose()
    {
        var registry = new RpcRegistry();
        (_, InMemoryConnection innerA) = InMemoryConnection.CreatePair();
        (_, InMemoryConnection innerB) = InMemoryConnection.CreatePair();
        var transport = new FakeServerTransport();
        transport.Enqueue(new InMemoryServerConnection(innerA, 1));
        transport.Enqueue(new InMemoryServerConnection(innerB, 2));
        var server = new RpcServer(transport, registry);

        var disconnected = new List<ulong>();
        var bothGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int connected = 0;
        server.ClientConnected += peer =>
        {
            if (Interlocked.Increment(ref connected) == 2)
            {
                bothGate.TrySetResult();
            }
        };
        server.ClientDisconnected += peer =>
        {
            lock (disconnected)
            {
                disconnected.Add(peer.ClientId);
            }
        };

        server.Start();
        await bothGate.Task;

        innerA.IsConnectedValue = false;
        server.CheckTimeouts(0);
        server.Close();

        Assert.Contains(1UL, disconnected);
        Assert.Contains(2UL, disconnected);
    }

    [Fact]
    public void ServerGetPeerReturnsNullForUnknownClient()
    {
        (RpcServer server, _, _, _, _) = StartServer(7);

        Assert.Null(server.GetPeer(99));
    }

    [Fact]
    public async Task ServerDisconnectedEventRemovesPeerAndFaults()
    {
        (RpcServer server, _, InMemoryConnection inner, _, TaskCompletionSource acceptGate) = StartServer(7);
        await acceptGate.Task;
        RpcPeer serverPeer = server.GetPeer(7)!;

        Task<RpcResult<Handshake>> call = serverPeer.InvokeAsync<Handshake, Handshake>(999, new Handshake(1));

        inner.Disconnect();

        RpcResult<Handshake> result = await call;
        Assert.Equal((int)RpcErrorCode.Unavailable, result.ErrorCode);
        Assert.Null(server.GetPeer(7));
    }

    [Fact]
    public async Task ServerPrunesDisconnectedPeers()
    {
        (RpcServer server, _, InMemoryConnection inner, _, TaskCompletionSource acceptGate) = StartServer(7);
        await acceptGate.Task;
        RpcPeer serverPeer = server.GetPeer(7)!;

        Task<RpcResult<Handshake>> call = serverPeer.InvokeAsync<Handshake, Handshake>(999, new Handshake(1));

        // A dead connection without a disconnect event. The prune sweep finds it.
        inner.IsConnectedValue = false;
        server.CheckTimeouts(0);

        RpcResult<Handshake> result = await call;
        Assert.Equal((int)RpcErrorCode.Unavailable, result.ErrorCode);
        Assert.Null(server.GetPeer(7));
    }

    [Fact]
    public async Task ServerCloseFaultsAndStopsAccepting()
    {
        (RpcServer server, _, _, _, TaskCompletionSource acceptGate) = StartServer(7);
        await acceptGate.Task;
        RpcPeer serverPeer = server.GetPeer(7)!;

        Task<RpcResult<Handshake>> call = serverPeer.InvokeAsync<Handshake, Handshake>(999, new Handshake(1));
        server.Close();

        RpcResult<Handshake> result = await call;
        Assert.Equal((int)RpcErrorCode.Unavailable, result.ErrorCode);
        Assert.Null(server.GetPeer(7));
    }

    [Fact]
    public async Task ServerPollChecksTimeouts()
    {
        long now = 0;
        var options = new RpcOptions { TimestampNanos = () => now };
        (RpcServer server, _, _, _, TaskCompletionSource acceptGate) = StartServer(7, options);
        await acceptGate.Task;
        RpcPeer serverPeer = server.GetPeer(7)!;

        Task<RpcResult<Handshake>> call = serverPeer.InvokeAsync<Handshake, Handshake>(
            999, new Handshake(1), TimeSpan.FromSeconds(5));

        now = 5_000_000_000;
        server.Poll();

        Assert.Equal((int)RpcErrorCode.RequestTimeout, (await call).ErrorCode);
    }

    [Fact]
    public void ServerRejectsNullArgumentsAndDoubleStart()
    {
        var transport = new FakeServerTransport();

        Assert.Throws<ArgumentNullException>(() => new RpcServer(null!, new RpcRegistry()));
        Assert.Throws<ArgumentNullException>(() => new RpcServer(transport, null!));

        var server = new RpcServer(transport, new RpcRegistry());
        server.Start();
        Assert.Throws<InvalidOperationException>(() => server.Start());
    }
}
