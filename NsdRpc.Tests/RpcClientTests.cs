using NsdRpc;
using NsdRpc.Wire;

namespace NsdRpc.Tests;

public sealed class RpcClientTests
{
    [RpcMethod(200)]
    private sealed class EchoHandler : IRpcHandler<Handshake, Handshake>
    {
        public ValueTask<RpcResult<Handshake>> ExecuteAsync(
            Handshake request, RpcContext context, CancellationToken cancellationToken)
        {
            return new ValueTask<RpcResult<Handshake>>(
                RpcResult<Handshake>.Success(new Handshake(request.ProtocolVersion + 1)));
        }
    }

    [RpcMethod(201)]
    private sealed class CaptureContextHandler : IRpcHandler<Handshake, Handshake>
    {
        public RpcContext Captured;

        public ValueTask<RpcResult<Handshake>> ExecuteAsync(
            Handshake request, RpcContext context, CancellationToken cancellationToken)
        {
            Captured = context;
            return new ValueTask<RpcResult<Handshake>>(
                RpcResult<Handshake>.Success(new Handshake(request.ProtocolVersion + 10)));
        }
    }

    [RpcMethod(203)]
    private sealed class StreamingHandler : IRpcHandler<Handshake, Handshake>
    {
        public ValueTask<RpcResult<Handshake>> ExecuteAsync(
            Handshake request, RpcContext context, CancellationToken cancellationToken)
        {
            context.Stream(new Handshake(10));
            context.Stream(new Handshake(20));
            context.Respond(RpcResult<Handshake>.Success(new Handshake(30)));
            return new ValueTask<RpcResult<Handshake>>(RpcResult<Handshake>.Success(default));
        }
    }

    private sealed class RecordingSink : IChunkSink<Handshake>
    {
        public List<Handshake> Chunks { get; } = [];

        public void OnChunk(in Handshake chunk)
        {
            Chunks.Add(chunk);
        }
    }

    [Fact]
    public async Task RpcClientInvokeRoundsTrip()
    {
        var serverRegistry = new RpcRegistry();
        serverRegistry.Add(new EchoHandler());
        (InMemoryConnection clientConn, InMemoryConnection serverConn) = InMemoryConnection.CreatePair();
        var client = new RpcClient(clientConn, new RpcRegistry());
        var server = new RpcPeer(serverConn);
        TaskCompletionSource gate = RpcTestHelpers.AttachDispatcher(server, serverRegistry);

        RpcResult<Handshake> result = await RpcTestHelpers.RoundTrip(
            clientConn, serverConn, client, server, gate, 200, new Handshake(41));

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value.ProtocolVersion);
    }

    [Fact]
    public async Task RpcClientHandlesServerToClientCalls()
    {
        var handler = new CaptureContextHandler();
        var clientRegistry = new RpcRegistry();
        clientRegistry.Add(handler);
        (InMemoryConnection clientConn, InMemoryConnection serverConn) = InMemoryConnection.CreatePair();
        var client = new RpcClient(clientConn, clientRegistry);
        var server = new RpcPeer(serverConn);

        // The response lands in the server connection only after the client
        // handler returns. Wait for the frame, then pump.
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        serverConn.FrameDelivered += () => delivered.TrySetResult();

        Task<RpcResult<Handshake>> call = server.InvokeAsync<Handshake, Handshake>(201, new Handshake(5));
        RpcTestHelpers.Pump(clientConn, client);
        await delivered.Task;
        RpcTestHelpers.Pump(serverConn, server);

        RpcResult<Handshake> result = await call;
        Assert.True(result.IsSuccess);
        Assert.Equal(15, result.Value.ProtocolVersion);
        Assert.Equal(0UL, handler.Captured.ClientId);
        Assert.False(handler.Captured.IsServer);
        Assert.Same(clientConn, handler.Captured.Connection);
    }

    [Fact]
    public void RpcClientNotifyForwards()
    {
        (InMemoryConnection clientConn, _) = InMemoryConnection.CreatePair();
        var client = new RpcClient(clientConn, new RpcRegistry());

        bool sent = client.Notify(200, new Handshake(1));

        Assert.True(sent);
        Envelope frame = Envelope.Deserialize((ReadOnlySpan<byte>)clientConn.SentFrames[0].AsSpan(4));
        Assert.Equal((byte)RpcMessageKind.Notification, frame.Kind);
        Assert.Equal(200UL, frame.Method);
    }

    [Fact]
    public async Task RpcClientPollChecksTimeouts()
    {
        long now = 0;
        var options = new RpcOptions { TimestampNanos = () => now };
        (InMemoryConnection clientConn, _) = InMemoryConnection.CreatePair();
        var client = new RpcClient(clientConn, new RpcRegistry(), options);

        Task<RpcResult<Handshake>> call = client.InvokeAsync<Handshake, Handshake>(
            200, new Handshake(1), TimeSpan.FromSeconds(5));

        now = 5_000_000_000;
        client.Poll();

        Assert.Equal((int)RpcErrorCode.RequestTimeout, (await call).ErrorCode);
    }

    [Fact]
    public void RpcClientRejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new RpcClient(null!, new RpcRegistry()));
        Assert.Throws<ArgumentNullException>(() => new RpcClient(new InMemoryConnection(), null!));
    }

    [Fact]
    public async Task RpcClientStreamSessionRoundsTrip()
    {
        var serverRegistry = new RpcRegistry();
        serverRegistry.Add(new StreamingHandler());
        (InMemoryConnection clientConn, InMemoryConnection serverConn) = InMemoryConnection.CreatePair();
        var client = new RpcClient(clientConn, new RpcRegistry());
        var server = new RpcPeer(serverConn);
        TaskCompletionSource gate = RpcTestHelpers.AttachDispatcher(server, serverRegistry);

        RpcStreamSession<Handshake, Handshake> session =
            client.InvokeStreamAsync<Handshake, Handshake, Handshake>(203, new Handshake(1));
        RpcTestHelpers.Pump(serverConn, server);
        await gate.Task.WaitAsync(TimeSpan.FromSeconds(10));
        RpcTestHelpers.Pump(clientConn, client);

        var received = new List<Handshake>();
        await foreach (Handshake chunk in session.Chunks)
        {
            received.Add(chunk);
        }

        RpcResult<Handshake> result = await session.Response;
        Assert.True(result.IsSuccess);
        Assert.Equal(30, result.Value.ProtocolVersion);
        Assert.Equal([10, 20], received.Select(chunk => chunk.ProtocolVersion).ToArray());
    }

    [Fact]
    public async Task RpcClientSinkStreamRoundsTrip()
    {
        var serverRegistry = new RpcRegistry();
        serverRegistry.Add(new StreamingHandler());
        (InMemoryConnection clientConn, InMemoryConnection serverConn) = InMemoryConnection.CreatePair();
        var client = new RpcClient(clientConn, new RpcRegistry());
        var server = new RpcPeer(serverConn);
        TaskCompletionSource gate = RpcTestHelpers.AttachDispatcher(server, serverRegistry);
        var sink = new RecordingSink();

        Task<RpcResult<Handshake>> call = client.InvokeStreamAsync<Handshake, Handshake, Handshake>(
            203, new Handshake(1), sink);
        RpcTestHelpers.Pump(serverConn, server);
        await gate.Task.WaitAsync(TimeSpan.FromSeconds(10));
        RpcTestHelpers.Pump(clientConn, client);

        RpcResult<Handshake> result = await call;
        Assert.True(result.IsSuccess);
        Assert.Equal(30, result.Value.ProtocolVersion);
        Assert.Equal([10, 20], sink.Chunks.Select(chunk => chunk.ProtocolVersion).ToArray());
    }

    [Fact]
    public void RpcClientNotifyWithoutPayloadForwards()
    {
        (InMemoryConnection clientConn, _) = InMemoryConnection.CreatePair();
        var client = new RpcClient(clientConn, new RpcRegistry());

        bool sent = client.Notify(200);

        Assert.True(sent);
        Envelope frame = Envelope.Deserialize((ReadOnlySpan<byte>)clientConn.SentFrames[0].AsSpan(4));
        Assert.Equal((byte)RpcMessageKind.Notification, frame.Kind);
        Assert.Equal(200UL, frame.Method);
        Assert.Null(frame.Payload);
    }

    [Fact]
    public async Task RpcClientCheckTimeoutsForwards()
    {
        long now = 0;
        var options = new RpcOptions { TimestampNanos = () => now };
        (InMemoryConnection clientConn, _) = InMemoryConnection.CreatePair();
        var client = new RpcClient(clientConn, new RpcRegistry(), options);

        Task<RpcResult<Handshake>> call = client.InvokeAsync<Handshake, Handshake>(
            200, new Handshake(1), TimeSpan.FromSeconds(5));

        now = 5_000_000_000;
        client.CheckTimeouts(now);

        Assert.Equal((int)RpcErrorCode.RequestTimeout, (await call).ErrorCode);
    }
}
