using NsdRpc;
using NsdRpc.Tcp;
using NsdRpc.Wire;

namespace NsdRpc.Tests;

public sealed class RpcHostTests
{
    private const ulong EchoMethod = 202;
    private const ulong FailMethod = 203;
    private const ulong NotifyMethod = 204;
    private const ulong StreamMethod = 205;

    private sealed class EchoHandler : IRpcHandler<Handshake, Handshake>
    {
        public ValueTask<RpcResult<Handshake>> ExecuteAsync(
            Handshake request, RpcContext context, CancellationToken cancellationToken)
        {
            return new ValueTask<RpcResult<Handshake>>(
                RpcResult<Handshake>.Success(new Handshake(request.ProtocolVersion + 1)));
        }
    }

    private sealed class FailHandler : IRpcHandler<Handshake, Handshake>
    {
        public ValueTask<RpcResult<Handshake>> ExecuteAsync(
            Handshake request, RpcContext context, CancellationToken cancellationToken)
        {
            return new ValueTask<RpcResult<Handshake>>(
                RpcResult<Handshake>.Failure(-32100, "app failure"));
        }
    }

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

    private sealed class StreamHandler : IRpcHandler<Handshake, Handshake>
    {
        public ValueTask<RpcResult<Handshake>> ExecuteAsync(
            Handshake request, RpcContext context, CancellationToken cancellationToken)
        {
            context.Stream(new Handshake(1));
            context.Stream(new Handshake(2));
            return new ValueTask<RpcResult<Handshake>>(RpcResult<Handshake>.Success(request));
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
    public async Task EmptyHostFailsCallsAndNotify()
    {
        var server = new RpcServer(new FakeServerTransport(), new RpcRegistry());

        RpcResult<Handshake> result =
            await server.InvokeAsync<Handshake, Handshake>(EchoMethod, new Handshake(1));

        Assert.Empty(server.Peers);
        Assert.False(result.IsSuccess);
        Assert.Equal((int)RpcErrorCode.Unavailable, result.ErrorCode);
        Assert.False(server.Notify(NotifyMethod));
        Assert.False(server.Notify<Handshake>(NotifyMethod, new Handshake(1)));
        server.Close();
    }

    [Fact]
    public async Task BroadcastInvokeReturnsTheLastSuccess()
    {
        (RpcServer server, List<RpcClient> _) = await StartAsync(2, static (registry, index) =>
            registry.Add<Handshake, Handshake>(new EchoHandler(), EchoMethod));

        RpcResult<Handshake> result = await server
            .InvokeAsync<Handshake, Handshake>(EchoMethod, new Handshake(1))
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(2, server.Peers.Count);
        Assert.True(result.IsSuccess, "code=" + result.ErrorCode + " message=" + result.ErrorMessage);
        Assert.Equal(2, result.Value.ProtocolVersion);
        server.Close();
    }

    [Fact]
    public async Task BroadcastInvokeReturnsTheFirstFailure()
    {
        (RpcServer server, List<RpcClient> _) = await StartAsync(2, static (registry, index) =>
            registry.Add<Handshake, Handshake>(new FailHandler(), FailMethod));

        RpcResult<Handshake> result = await server
            .InvokeAsync<Handshake, Handshake>(FailMethod, new Handshake(1))
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(result.IsSuccess);
        Assert.Equal(-32100, result.ErrorCode);
        server.Close();
    }

    [Fact]
    public async Task BroadcastNotifyReachesEveryPeer()
    {
        var gates = new List<TaskCompletionSource>();
        (RpcServer server, List<RpcClient> _) = await StartAsync(2, (registry, index) =>
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            gates.Add(gate);
            registry.Add<Handshake, Handshake>(new SignalingHandler(gate), NotifyMethod);
        });

        bool accepted = server.Notify<Handshake>(NotifyMethod, new Handshake(1));

        Assert.True(accepted);
        Assert.Equal(2, gates.Count);
        foreach (TaskCompletionSource gate in gates)
        {
            await gate.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        server.Close();
    }

    [Fact]
    public async Task BroadcastStreamFansInEveryChunk()
    {
        (RpcServer server, List<RpcClient> _) = await StartAsync(2, static (registry, index) =>
            registry.Add<Handshake, Handshake>(new StreamHandler(), StreamMethod));

        RpcStreamSession<Handshake, Handshake> session = server
            .InvokeStreamAsync<Handshake, Handshake, Handshake>(StreamMethod, new Handshake(1));

        int chunkCount = 0;
        await foreach (Handshake chunk in session.Chunks)
        {
            chunkCount++;
        }

        RpcResult<Handshake> response = await session.Response.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(response.IsSuccess);
        Assert.Equal(4, chunkCount);
        server.Close();
    }

    private static async Task<(RpcServer Server, List<RpcClient> Clients)> StartAsync(
        int clientCount, Action<RpcRegistry, int> configureClient)
    {
        var serverRegistry = new RpcRegistry();
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
