using NsdRpc;
using NsdRpc.Tcp;
using NsdRpc.Wire;

namespace NsdRpc.Tests;

public sealed class RpcHostTests
{
    private const ulong EchoMethod = 202;
    private const ulong TriggerMethod = 204;
    private const ulong FanOutMethod = 205;

    private sealed class SignalingHandler : IRpcHandler<Handshake, Handshake>
    {
        private readonly TaskCompletionSource _gate;

        public SignalingHandler(TaskCompletionSource gate)
        {
            _gate = gate;
        }

        public ValueTask<RpcResult<Handshake>> ExecuteAsync(
            Handshake request, RpcContext context, CancellationToken cancellationToken)
        {
            _gate.TrySetResult();
            return new ValueTask<RpcResult<Handshake>>(RpcResult<Handshake>.Success(request));
        }
    }

    // A server handler that fans out to every peer through the context. It
    // holds no peer set, so one instance serves any server.
    private sealed class FanOutHandler : IRpcHandler<Handshake, Handshake>
    {
        public ValueTask<RpcResult<Handshake>> ExecuteAsync(
            Handshake request, RpcContext context, CancellationToken cancellationToken)
        {
            foreach (RpcPeer peer in context.Host.Peers)
            {
                peer.Notify(FanOutMethod, in request);
            }

            return new ValueTask<RpcResult<Handshake>>(RpcResult<Handshake>.Success(request));
        }
    }

    private sealed class CountingEchoHandler : IRpcHandler<Handshake, Handshake>
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public ValueTask<RpcResult<Handshake>> ExecuteAsync(
            Handshake request, RpcContext context, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _count);
            return new ValueTask<RpcResult<Handshake>>(
                RpcResult<Handshake>.Success(new Handshake(request.ProtocolVersion + 1)));
        }
    }

    [Fact]
    public void PeerAndClientExposeASinglePeer()
    {
        var peer = new RpcPeer(new InMemoryConnection());
        Assert.Same(peer, Assert.Single(peer.Peers));

        (InMemoryConnection clientConn, _) = InMemoryConnection.CreatePair();
        var client = new RpcClient(clientConn, new RpcRegistry());
        Assert.Single(client.Peers);
    }

    [Fact]
    public void PeerSetDefaultIsEmpty()
    {
        PeerSet peers = default;

        Assert.Empty(peers);
        int seen = 0;
        foreach (RpcPeer _ in peers)
        {
            seen++;
        }

        Assert.Equal(0, seen);
    }

    [Fact]
    public void PeerSetIterationDoesNotAllocate()
    {
        var peer = new RpcPeer(new InMemoryConnection());
        PeerSet peers = peer.Peers;

        int seen = 0;
        foreach (RpcPeer _ in peers)
        {
            seen++;
        }

        Assert.Equal(1, seen);

        // Warm up, then measure.
        foreach (RpcPeer _ in peers)
        {
            seen++;
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1000; index++)
        {
            foreach (RpcPeer _ in peers)
            {
                seen++;
            }
        }

        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(before, after);
    }

    [Fact]
    public void EmptyServerExposesNoPeers()
    {
        var server = new RpcServer(new FakeServerTransport(), new RpcRegistry());

        Assert.Empty(server.Peers);
        Assert.Null(server.GetPeer(1));
        server.Close();
    }

    [Fact]
    public async Task HandlerFansOutThroughContextHost()
    {
        var gates = new List<TaskCompletionSource>();
        (RpcServer server, List<RpcClient> clients) = await StartAsync(
            2,
            (registry, index) =>
            {
                var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                gates.Add(gate);
                registry.Add<Handshake, Handshake>(new SignalingHandler(gate), FanOutMethod);
            },
            serverRegistry => serverRegistry.Add<Handshake, Handshake>(new FanOutHandler(), TriggerMethod));

        Assert.Equal(2, gates.Count);
        bool sent = clients[0].Notify(TriggerMethod, new Handshake(1));
        Assert.True(sent);

        foreach (TaskCompletionSource gate in gates)
        {
            await gate.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        server.Close();
    }

    [Fact]
    public async Task CallerTargetsOnePeer()
    {
        var handlers = new List<CountingEchoHandler>();
        (RpcServer server, List<RpcClient> _) = await StartAsync(2, (registry, index) =>
        {
            var handler = new CountingEchoHandler();
            handlers.Add(handler);
            registry.Add<Handshake, Handshake>(handler, EchoMethod);
        });

        RpcPeer target = server.Peers[0];
        RpcResult<Handshake> result = await target
            .InvokeAsync<Handshake, Handshake>(EchoMethod, new Handshake(1))
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(result.IsSuccess, "code=" + result.ErrorCode + " message=" + result.ErrorMessage);
        Assert.Equal(2, result.Value.ProtocolVersion);

        // Exactly one client ran the handler. The server reached one peer.
        int total = 0;
        foreach (CountingEchoHandler handler in handlers)
        {
            total += handler.Count;
        }

        Assert.Equal(1, total);
        server.Close();
    }

    [Fact]
    public async Task PeerSetSnapshotIsImmutable()
    {
        var transport = new TcpServerTransport(new RpcOptions(), new TcpOptions());
        var server = new RpcServer(transport, new RpcRegistry());
        int connected = 0;
        var firstConnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondConnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.ClientConnected += _ =>
        {
            int count = Interlocked.Increment(ref connected);
            if (count == 1)
            {
                firstConnected.TrySetResult();
            }
            else if (count == 2)
            {
                secondConnected.TrySetResult();
            }
        };
        server.Start();

        var firstConnection = new TcpClientConnection(
            new RpcOptions(), new TcpOptions { Host = "127.0.0.1", Port = transport.Port });
        var firstClient = new RpcClient(firstConnection, new RpcRegistry());
        await firstConnection.ConnectAsync();
        await firstConnected.Task.WaitAsync(TimeSpan.FromSeconds(10));

        PeerSet captured = server.Peers;
        Assert.Single(captured);

        var secondConnection = new TcpClientConnection(
            new RpcOptions(), new TcpOptions { Host = "127.0.0.1", Port = transport.Port });
        var secondClient = new RpcClient(secondConnection, new RpcRegistry());
        await secondConnection.ConnectAsync();
        await secondConnected.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // The captured snapshot is unchanged. A fresh read sees the new peer.
        Assert.Single(captured);
        Assert.Equal(2, server.Peers.Count);

        // A client host exposes its one peer.
        Assert.Single(firstClient.Peers);
        Assert.Single(secondClient.Peers);
        server.Close();
    }

    private static async Task<(RpcServer Server, List<RpcClient> Clients)> StartAsync(
        int clientCount,
        Action<RpcRegistry, int> configureClient,
        Action<RpcRegistry>? configureServer = null)
    {
        var serverRegistry = new RpcRegistry();
        configureServer?.Invoke(serverRegistry);
        serverRegistry.Freeze();
        var transport = new TcpServerTransport(new RpcOptions(), new TcpOptions());
        var server = new RpcServer(transport, serverRegistry);
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int connectedCount = 0;
        server.ClientConnected += _ =>
        {
            if (Interlocked.Increment(ref connectedCount) == clientCount)
            {
                connected.TrySetResult();
            }
        };
        server.Start();

        var clients = new List<RpcClient>();
        for (int index = 0; index < clientCount; index++)
        {
            var registry = new RpcRegistry();
            configureClient(registry, index);
            var connection = new TcpClientConnection(
                new RpcOptions(), new TcpOptions { Host = "127.0.0.1", Port = transport.Port });
            var client = new RpcClient(connection, registry);
            await connection.ConnectAsync();
            clients.Add(client);
        }

        await connected.Task.WaitAsync(TimeSpan.FromSeconds(10));
        return (server, clients);
    }
}
