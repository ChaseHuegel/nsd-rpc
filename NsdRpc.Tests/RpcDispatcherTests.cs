using NsdRpc;
using NsdRpc.Wire;

namespace NsdRpc.Tests;

public sealed class RpcDispatcherTests
{
    [RpcMethod(5)]
    private sealed class EchoHandler : IRpcHandler<Handshake, Handshake>
    {
        public ValueTask<RpcResult<Handshake>> ExecuteAsync(
            Handshake request, RpcContext context, CancellationToken cancellationToken)
        {
            return new ValueTask<RpcResult<Handshake>>(
                RpcResult<Handshake>.Success(new Handshake(request.ProtocolVersion + 1)));
        }
    }

    [RpcMethod(6)]
    private sealed class ThrowingHandler : IRpcHandler<Handshake, Handshake>
    {
        public ValueTask<RpcResult<Handshake>> ExecuteAsync(
            Handshake request, RpcContext context, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("secret detail");
        }
    }

    [RpcMethod(7)]
    private sealed class ReservedCodeHandler : IRpcHandler<Handshake, Handshake>
    {
        public ValueTask<RpcResult<Handshake>> ExecuteAsync(
            Handshake request, RpcContext context, CancellationToken cancellationToken)
        {
            return new ValueTask<RpcResult<Handshake>>(RpcResult<Handshake>.Failure(-1, "reserved code"));
        }
    }

    [RpcMethod(8)]
    private sealed class AppCodeHandler : IRpcHandler<Handshake, Handshake>
    {
        public ValueTask<RpcResult<Handshake>> ExecuteAsync(
            Handshake request, RpcContext context, CancellationToken cancellationToken)
        {
            return new ValueTask<RpcResult<Handshake>>(RpcResult<Handshake>.Failure(-32100, "app code"));
        }
    }

    [RpcMethod(9)]
    private sealed class SelfRespondHandler : IRpcHandler<Handshake, Handshake>
    {
        public ValueTask<RpcResult<Handshake>> ExecuteAsync(
            Handshake request, RpcContext context, CancellationToken cancellationToken)
        {
            context.Respond(RpcResult<Handshake>.Success(new Handshake(77)));
            return new ValueTask<RpcResult<Handshake>>(RpcResult<Handshake>.Success(new Handshake(88)));
        }
    }

    [RpcMethod(10, RpcDirection.ClientToServer)]
    private sealed class ClientToServerHandler : IRpcHandler<Handshake, Handshake>
    {
        public ValueTask<RpcResult<Handshake>> ExecuteAsync(
            Handshake request, RpcContext context, CancellationToken cancellationToken)
        {
            return new ValueTask<RpcResult<Handshake>>(
                RpcResult<Handshake>.Success(new Handshake(request.ProtocolVersion + 100)));
        }
    }

    [RpcMethod(11, RpcDirection.ServerToClient)]
    private sealed class ServerToClientHandler : IRpcHandler<Handshake, Handshake>
    {
        public ValueTask<RpcResult<Handshake>> ExecuteAsync(
            Handshake request, RpcContext context, CancellationToken cancellationToken)
        {
            return new ValueTask<RpcResult<Handshake>>(
                RpcResult<Handshake>.Success(new Handshake(request.ProtocolVersion + 200)));
        }
    }

    [RpcMethod(12)]
    private sealed class TokenHandler : IRpcHandler<Handshake, Handshake>
    {
        public CancellationToken Captured;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<RpcResult<Handshake>> ExecuteAsync(
            Handshake request, RpcContext context, CancellationToken cancellationToken)
        {
            Captured = cancellationToken;
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return RpcResult<Handshake>.Success(default);
        }
    }

    [Fact]
    public async Task DispatcherAnswersMethodNotFound()
    {
        (InMemoryConnection clientConn, InMemoryConnection serverConn) = InMemoryConnection.CreatePair();
        var client = new RpcPeer(clientConn);
        var server = new RpcPeer(serverConn);
        TaskCompletionSource gate = RpcTestHelpers.AttachDispatcher(server, new RpcRegistry());

        RpcResult<Handshake> result = await RpcTestHelpers.RoundTrip(
            clientConn, serverConn, client, server, gate, 5, new Handshake(1));

        Assert.False(result.IsSuccess);
        Assert.Equal((int)RpcErrorCode.MethodNotFound, result.ErrorCode);
    }

    [Fact]
    public async Task DispatcherAnswersInvalidParams()
    {
        var registry = new RpcRegistry();
        registry.Add(new EchoHandler());
        (InMemoryConnection clientConn, InMemoryConnection serverConn) = InMemoryConnection.CreatePair();
        var client = new RpcPeer(clientConn);
        var server = new RpcPeer(serverConn);
        TaskCompletionSource gate = RpcTestHelpers.AttachDispatcher(server, registry);

        // Field id 0 announces a 4-byte int that never arrives. Truncated.
        var envelope = new Envelope(1, (byte)RpcMessageKind.Request, 1UL, 5UL, [0, 0], null);
        RpcTestHelpers.Deliver(server, in envelope);
        await gate.Task;

        Envelope decoded = Envelope.Deserialize((ReadOnlySpan<byte>)serverConn.SentFrames[0].AsSpan(4));
        Assert.Equal((byte)RpcMessageKind.Response, decoded.Kind);
        Assert.NotNull(decoded.Error);
        Assert.Equal((int)RpcErrorCode.InvalidParams, decoded.Error.Value.Code);
    }

    [Fact]
    public async Task DispatcherMapsHandlerExceptionToInternalError()
    {
        var registry = new RpcRegistry();
        registry.Add(new ThrowingHandler());
        (InMemoryConnection clientConn, InMemoryConnection serverConn) = InMemoryConnection.CreatePair();
        var client = new RpcPeer(clientConn);
        var server = new RpcPeer(serverConn);
        TaskCompletionSource gate = RpcTestHelpers.AttachDispatcher(server, registry);

        RpcResult<Handshake> result = await RpcTestHelpers.RoundTrip(
            clientConn, serverConn, client, server, gate, 6, new Handshake(1));

        Assert.False(result.IsSuccess);
        Assert.Equal((int)RpcErrorCode.InternalError, result.ErrorCode);
        Assert.DoesNotContain("secret", result.ErrorMessage);
    }

    [Fact]
    public async Task DispatcherRejectsReservedErrorCodesAndPassesAppCodes()
    {
        var registry = new RpcRegistry();
        registry.Add(new ReservedCodeHandler());
        registry.Add(new AppCodeHandler());
        (InMemoryConnection clientConn, InMemoryConnection serverConn) = InMemoryConnection.CreatePair();
        var client = new RpcPeer(clientConn);
        var server = new RpcPeer(serverConn);

        TaskCompletionSource reservedGate = RpcTestHelpers.AttachDispatcher(server, registry);
        RpcResult<Handshake> reserved = await RpcTestHelpers.RoundTrip(
            clientConn, serverConn, client, server, reservedGate, 7, new Handshake(1));

        TaskCompletionSource appGate = RpcTestHelpers.AttachDispatcher(server, registry);
        RpcResult<Handshake> app = await RpcTestHelpers.RoundTrip(
            clientConn, serverConn, client, server, appGate, 8, new Handshake(1));

        Assert.Equal((int)RpcErrorCode.InternalError, reserved.ErrorCode);
        Assert.Equal(-32100, app.ErrorCode);
        Assert.Equal("app code", app.ErrorMessage);
    }

    [Fact]
    public async Task DispatchAllocatesOnlyTheCancellationSource()
    {
        var registry = new RpcRegistry();
        registry.Add(new EchoHandler());
        var peer = new RpcPeer(new InMemoryConnection());
        var dispatcher = new RpcDispatcher(registry, peer);
        var context = new RpcContext(peer, 1UL, 5UL);
        byte[] payload = new Handshake(1).Serialize();

        await dispatcher.DispatchAsync(context, payload);
        await dispatcher.DispatchAsync(context, payload);

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        await dispatcher.DispatchAsync(context, payload);
        long allocatedAfter = GC.GetAllocatedBytesForCurrentThread();

        Assert.True(
            allocatedAfter - allocatedBefore <= 384,
            "allocated " + (allocatedAfter - allocatedBefore));
    }

    [Fact]
    public async Task HandlerTokenFiresOnCancellation()
    {
        var registry = new RpcRegistry();
        var handler = new TokenHandler();
        registry.Add(handler);
        (InMemoryConnection clientConn, InMemoryConnection serverConn) = InMemoryConnection.CreatePair();
        var client = new RpcPeer(clientConn);
        var server = new RpcPeer(serverConn);
        server.DispatchHook = new RpcDispatcher(registry, server).DispatchAsync;

        Task<RpcResult<Handshake>> call = client.InvokeAsync<Handshake, Handshake>(12, new Handshake(1));
        RpcTestHelpers.Pump(serverConn, server);
        await handler.Started.Task;

        var cancellation = new Envelope(1, (byte)RpcMessageKind.Cancellation, 1UL, 0UL, null, null);
        RpcTestHelpers.Deliver(server, in cancellation);

        Assert.True(handler.Captured.IsCancellationRequested);
        Assert.False(call.IsCompleted);
    }

    [Fact]
    public async Task HandlerTokenFiresOnFault()
    {
        var registry = new RpcRegistry();
        var handler = new TokenHandler();
        registry.Add(handler);
        (InMemoryConnection clientConn, InMemoryConnection serverConn) = InMemoryConnection.CreatePair();
        var client = new RpcPeer(clientConn);
        var server = new RpcPeer(serverConn);
        server.DispatchHook = new RpcDispatcher(registry, server).DispatchAsync;

        Task<RpcResult<Handshake>> call = client.InvokeAsync<Handshake, Handshake>(12, new Handshake(1));
        RpcTestHelpers.Pump(serverConn, server);
        await handler.Started.Task;

        serverConn.Disconnect();
        Assert.True(handler.Captured.IsCancellationRequested);

        clientConn.Disconnect();
        RpcResult<Handshake> result = await call;
        Assert.Equal((int)RpcErrorCode.Unavailable, result.ErrorCode);
    }

    [Fact]
    public async Task HandlerMayRespondItselfAndDispatcherSkips()
    {
        var registry = new RpcRegistry();
        registry.Add(new SelfRespondHandler());
        (InMemoryConnection clientConn, InMemoryConnection serverConn) = InMemoryConnection.CreatePair();
        var client = new RpcPeer(clientConn);
        var server = new RpcPeer(serverConn);
        TaskCompletionSource gate = RpcTestHelpers.AttachDispatcher(server, registry);

        RpcResult<Handshake> result = await RpcTestHelpers.RoundTrip(
            clientConn, serverConn, client, server, gate, 9, new Handshake(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(77, result.Value.ProtocolVersion);
    }

    [Fact]
    public async Task HandlerReceivesDefaultForNullPayload()
    {
        var registry = new RpcRegistry();
        registry.Add(new EchoHandler());
        (InMemoryConnection clientConn, InMemoryConnection serverConn) = InMemoryConnection.CreatePair();
        var client = new RpcPeer(clientConn);
        var server = new RpcPeer(serverConn);
        TaskCompletionSource gate = RpcTestHelpers.AttachDispatcher(server, registry);

        var envelope = new Envelope(1, (byte)RpcMessageKind.Request, 1UL, 5UL, null, null);
        RpcTestHelpers.Deliver(server, in envelope);
        await gate.Task;

        Envelope decoded = Envelope.Deserialize((ReadOnlySpan<byte>)serverConn.SentFrames[0].AsSpan(4));
        Assert.Null(decoded.Error);
        Assert.NotNull(decoded.Payload);
        Handshake payload = Handshake.Deserialize((ReadOnlySpan<byte>)decoded.Payload);
        Assert.Equal(1, payload.ProtocolVersion);
    }

    [Fact]
    public async Task DispatcherEnforcesDirectionOnServer()
    {
        var registry = new RpcRegistry();
        registry.Add(new ClientToServerHandler());
        registry.Add(new ServerToClientHandler());
        (InMemoryConnection clientConn, InMemoryConnection inner) = InMemoryConnection.CreatePair();
        var serverConn = new InMemoryServerConnection(inner, 7UL);
        var client = new RpcPeer(clientConn);
        var server = new RpcPeer(serverConn);
        Assert.True(server.IsServer);
        Assert.Equal(7UL, server.ClientId);

        TaskCompletionSource allowedGate = RpcTestHelpers.AttachDispatcher(server, registry);
        RpcResult<Handshake> allowed = await RpcTestHelpers.RoundTrip(
            clientConn, inner, client, server, allowedGate, 10, new Handshake(1));

        TaskCompletionSource rejectedGate = RpcTestHelpers.AttachDispatcher(server, registry);
        RpcResult<Handshake> rejected = await RpcTestHelpers.RoundTrip(
            clientConn, inner, client, server, rejectedGate, 11, new Handshake(1));

        Assert.True(allowed.IsSuccess);
        Assert.Equal(101, allowed.Value.ProtocolVersion);
        Assert.Equal((int)RpcErrorCode.MethodNotFound, rejected.ErrorCode);
    }

    [Fact]
    public async Task DispatcherEnforcesDirectionOnClient()
    {
        var registry = new RpcRegistry();
        registry.Add(new ClientToServerHandler());
        registry.Add(new ServerToClientHandler());
        (InMemoryConnection clientConn, InMemoryConnection serverConn) = InMemoryConnection.CreatePair();
        var client = new RpcPeer(clientConn);
        Assert.False(client.IsServer);
        TaskCompletionSource gate = RpcTestHelpers.AttachDispatcher(client, registry);

        var serverToClient = new Envelope(1, (byte)RpcMessageKind.Request, 1UL, 11UL, null, null);
        RpcTestHelpers.Deliver(client, in serverToClient);
        await gate.Task;
        Assert.Null(Envelope.Deserialize((ReadOnlySpan<byte>)clientConn.SentFrames[0].AsSpan(4)).Error);

        TaskCompletionSource secondGate = RpcTestHelpers.AttachDispatcher(client, registry);
        var clientToServer = new Envelope(1, (byte)RpcMessageKind.Request, 2UL, 10UL, null, null);
        RpcTestHelpers.Deliver(client, in clientToServer);
        await secondGate.Task;
        Assert.Equal(
            (int)RpcErrorCode.MethodNotFound,
            Envelope.Deserialize((ReadOnlySpan<byte>)clientConn.SentFrames[1].AsSpan(4)).Error!.Value.Code);
    }

    [Fact]
    public async Task NotificationWithoutHandlerSendsNoResponse()
    {
        var registry = new RpcRegistry();
        registry.Add(new EchoHandler());
        (InMemoryConnection clientConn, InMemoryConnection serverConn) = InMemoryConnection.CreatePair();
        var client = new RpcPeer(clientConn);
        var server = new RpcPeer(serverConn);
        TaskCompletionSource gate = RpcTestHelpers.AttachDispatcher(server, registry);

        var notification = new Envelope(1, (byte)RpcMessageKind.Notification, 0UL, 99UL, null, null);
        RpcTestHelpers.Deliver(server, in notification);
        await gate.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Empty(serverConn.SentFrames);

        TaskCompletionSource secondGate = RpcTestHelpers.AttachDispatcher(server, registry);
        RpcResult<Handshake> result = await RpcTestHelpers.RoundTrip(
            clientConn, serverConn, client, server, secondGate, 5, new Handshake(1));
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task NotificationHandlerExceptionSendsNoResponse()
    {
        var registry = new RpcRegistry();
        registry.Add(new ThrowingHandler());
        (InMemoryConnection clientConn, InMemoryConnection serverConn) = InMemoryConnection.CreatePair();
        var client = new RpcPeer(clientConn);
        var server = new RpcPeer(serverConn);
        TaskCompletionSource gate = RpcTestHelpers.AttachDispatcher(server, registry);

        var notification = new Envelope(1, (byte)RpcMessageKind.Notification, 0UL, 6UL, null, null);
        RpcTestHelpers.Deliver(server, in notification);
        await gate.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Empty(serverConn.SentFrames);
    }
}
