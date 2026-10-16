using NsdRpc;
using NsdRpc.Wire;

namespace NsdRpc.Tests;

public sealed class RpcPeerTests
{
    private const ulong EchoMethod = 5;

    private const ulong ProbeMethod = 1000;

    private static void Pump(InMemoryConnection from, RpcPeer into)
    {
        while (from.TryDequeueInbound(out byte[] frame))
        {
            into.HandleFrame(frame);
        }
    }

    private static ValueTask EchoHook(RpcContext context, byte[]? payload)
    {
        Handshake request = Handshake.Deserialize((ReadOnlySpan<byte>)payload!);
        context.Respond(RpcResult<Handshake>.Success(new Handshake(request.ProtocolVersion + 1)));
        return default;
    }

    private static List<Envelope> DecodeFrames(IReadOnlyList<byte[]> frames)
    {
        var envelopes = new List<Envelope>(frames.Count);
        foreach (byte[] frame in frames)
        {
            envelopes.Add(Envelope.Deserialize((ReadOnlySpan<byte>)frame.AsSpan(4)));
        }

        return envelopes;
    }

    [Fact]
    public void PeerAllocatesSequentialNonzeroIds()
    {
        var connection = new InMemoryConnection();
        var peer = new RpcPeer(connection);

        peer.InvokeAsync<Handshake, Handshake>(EchoMethod, new Handshake(1));
        peer.InvokeAsync<Handshake, Handshake>(EchoMethod, new Handshake(2));
        peer.InvokeAsync<Handshake, Handshake>(EchoMethod, new Handshake(3));

        List<Envelope> sent = DecodeFrames(connection.SentFrames);
        Assert.Equal(3, sent.Count);
        Assert.Equal(1UL, sent[0].Id);
        Assert.Equal(2UL, sent[1].Id);
        Assert.Equal(3UL, sent[2].Id);
        Assert.All(sent, frame => Assert.Equal((byte)RpcMessageKind.Request, frame.Kind));
        Assert.All(sent, frame => Assert.Equal(EchoMethod, frame.Method));
    }

    [Fact]
    public async Task InvokeCompletesWithResponse()
    {
        (InMemoryConnection clientConn, InMemoryConnection serverConn) = InMemoryConnection.CreatePair();
        var client = new RpcPeer(clientConn);
        var server = new RpcPeer(serverConn);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.DispatchHook = (context, payload) =>
        {
            ValueTask response = EchoHook(context, payload);
            gate.TrySetResult();
            return response;
        };

        Task<RpcResult<Handshake>> call = client.InvokeAsync<Handshake, Handshake>(
            EchoMethod, new Handshake(42));

        Pump(serverConn, server);
        await gate.Task;
        Pump(clientConn, client);

        RpcResult<Handshake> result = await call;
        Assert.True(result.IsSuccess);
        Assert.Equal(43, result.Value.ProtocolVersion);
    }

    [Fact]
    public async Task ResponsesCompleteOutOfOrder()
    {
        (InMemoryConnection clientConn, InMemoryConnection serverConn) = InMemoryConnection.CreatePair();
        var client = new RpcPeer(clientConn);
        var server = new RpcPeer(serverConn);
        var calls = new List<RpcContext>();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.DispatchHook = (context, payload) =>
        {
            calls.Add(context);
            if (calls.Count == 2)
            {
                gate.TrySetResult();
            }

            return default;
        };

        Task<RpcResult<Handshake>> first = client.InvokeAsync<Handshake, Handshake>(EchoMethod, new Handshake(1));
        Task<RpcResult<Handshake>> second = client.InvokeAsync<Handshake, Handshake>(EchoMethod, new Handshake(2));

        Pump(serverConn, server);
        await gate.Task;

        // Answer the second call before the first. Order must not matter.
        calls[1].Respond(RpcResult<Handshake>.Success(new Handshake(20)));
        calls[0].Respond(RpcResult<Handshake>.Success(new Handshake(10)));

        Pump(clientConn, client);

        RpcResult<Handshake> firstResult = await first;
        RpcResult<Handshake> secondResult = await second;
        Assert.Equal(10, firstResult.Value.ProtocolVersion);
        Assert.Equal(20, secondResult.Value.ProtocolVersion);
    }

    [Fact]
    public async Task UnknownResponseIsDropped()
    {
        (InMemoryConnection clientConn, _) = InMemoryConnection.CreatePair();
        var client = new RpcPeer(clientConn);
        Task<RpcResult<Handshake>> call = client.InvokeAsync<Handshake, Handshake>(EchoMethod, new Handshake(1));

        var envelope = new Envelope(1, (byte)RpcMessageKind.Response, 999UL, 0UL, null, null);
        RpcTestHelpers.Deliver(client, in envelope);

        Assert.False(call.IsCompleted);
    }

    [Fact]
    public async Task DuplicateResponseIsDropped()
    {
        (InMemoryConnection clientConn, InMemoryConnection serverConn) = InMemoryConnection.CreatePair();
        var client = new RpcPeer(clientConn);
        var server = new RpcPeer(serverConn);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.DispatchHook = (context, payload) =>
        {
            ValueTask response = EchoHook(context, payload);
            gate.TrySetResult();
            return response;
        };

        Task<RpcResult<Handshake>> call = client.InvokeAsync<Handshake, Handshake>(
            EchoMethod, new Handshake(42));
        Pump(serverConn, server);
        await gate.Task;
        Pump(clientConn, client);
        Assert.True((await call).IsSuccess);

        byte[] responseFrame = serverConn.SentFrames[^1];
        client.HandleFrame(responseFrame);
        Assert.True(call.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task ResponseToNotificationIsDropped()
    {
        (InMemoryConnection clientConn, _) = InMemoryConnection.CreatePair();
        var client = new RpcPeer(clientConn);
        Task<RpcResult<Handshake>> call = client.InvokeAsync<Handshake, Handshake>(EchoMethod, new Handshake(1));

        var envelope = new Envelope(1, (byte)RpcMessageKind.Response, 0UL, 0UL, null, null);
        RpcTestHelpers.Deliver(client, in envelope);

        Assert.False(call.IsCompleted);
    }

    [Fact]
    public async Task CallTimesOutAndSendsCancellation()
    {
        long now = 0;
        var options = new RpcOptions { TimestampNanos = () => now };
        var connection = new InMemoryConnection();
        var peer = new RpcPeer(connection, options);

        Task<RpcResult<Handshake>> call = peer.InvokeAsync<Handshake, Handshake>(
            EchoMethod, new Handshake(1), TimeSpan.FromSeconds(5));

        now = 5_000_000_000;
        peer.CheckTimeouts(now);

        RpcResult<Handshake> result = await call;
        Assert.False(result.IsSuccess);
        Assert.Equal((int)RpcErrorCode.RequestTimeout, result.ErrorCode);

        List<Envelope> frames = DecodeFrames(connection.SentFrames);
        Assert.Equal(2, frames.Count);
        Assert.Equal((byte)RpcMessageKind.Request, frames[0].Kind);
        Assert.Equal((byte)RpcMessageKind.Cancellation, frames[1].Kind);
        Assert.Equal(frames[0].Id, frames[1].Id);
    }

    [Fact]
    public async Task TimeoutZeroDisablesDeadline()
    {
        long now = 0;
        var options = new RpcOptions { TimestampNanos = () => now };
        var peer = new RpcPeer(new InMemoryConnection(), options);

        Task<RpcResult<Handshake>> call = peer.InvokeAsync<Handshake, Handshake>(
            EchoMethod, new Handshake(1), TimeSpan.Zero);

        now = 100_000_000_000;
        peer.CheckTimeouts(now);

        Assert.False(call.IsCompleted);
    }

    [Fact]
    public async Task PerCallTimeoutOverridesDefault()
    {
        long now = 0;
        var options = new RpcOptions
        {
            Timeout = TimeSpan.FromSeconds(30),
            TimestampNanos = () => now,
        };
        var peer = new RpcPeer(new InMemoryConnection(), options);

        Task<RpcResult<Handshake>> shortCall = peer.InvokeAsync<Handshake, Handshake>(
            EchoMethod, new Handshake(1), TimeSpan.FromSeconds(5));
        Task<RpcResult<Handshake>> defaultCall = peer.InvokeAsync<Handshake, Handshake>(
            EchoMethod, new Handshake(2));

        now = 5_000_000_000;
        peer.CheckTimeouts(now);

        Assert.True(shortCall.IsCompleted);
        Assert.False(defaultCall.IsCompleted);
    }

    [Fact]
    public async Task TokenCancellationSendsCancellation()
    {
        var connection = new InMemoryConnection();
        var peer = new RpcPeer(connection);
        using var cancellation = new CancellationTokenSource();

        Task<RpcResult<Handshake>> call = peer.InvokeAsync<Handshake, Handshake>(
            EchoMethod, new Handshake(1), cancellationToken: cancellation.Token);
        cancellation.Cancel();

        RpcResult<Handshake> result = await call;
        Assert.False(result.IsSuccess);
        Assert.Equal((int)RpcErrorCode.Cancelled, result.ErrorCode);

        List<Envelope> frames = DecodeFrames(connection.SentFrames);
        Assert.Equal(2, frames.Count);
        Assert.Equal((byte)RpcMessageKind.Request, frames[0].Kind);
        Assert.Equal((byte)RpcMessageKind.Cancellation, frames[1].Kind);
    }

    [Fact]
    public async Task FullPendingTableRejectsWithBusy()
    {
        var options = new RpcOptions { MaxPendingCalls = 2 };
        var connection = new InMemoryConnection();
        var peer = new RpcPeer(connection, options);

        Task<RpcResult<Handshake>> first = peer.InvokeAsync<Handshake, Handshake>(EchoMethod, new Handshake(1));
        Task<RpcResult<Handshake>> second = peer.InvokeAsync<Handshake, Handshake>(EchoMethod, new Handshake(2));
        Task<RpcResult<Handshake>> third = peer.InvokeAsync<Handshake, Handshake>(EchoMethod, new Handshake(3));

        RpcResult<Handshake> result = await third;
        Assert.False(result.IsSuccess);
        Assert.Equal((int)RpcErrorCode.Busy, result.ErrorCode);
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        Assert.Equal(2, connection.SentFrames.Count);
    }

    [Fact]
    public async Task InboundOverflowAnswersBusy()
    {
        (InMemoryConnection clientConn, InMemoryConnection serverConn) = InMemoryConnection.CreatePair();
        var client = new RpcPeer(clientConn);
        var server = new RpcPeer(serverConn, new RpcOptions { MaxPendingCalls = 1 });
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.DispatchHook = (context, payload) =>
        {
            gate.TrySetResult();
            return default;
        };

        Task<RpcResult<Handshake>> first = client.InvokeAsync<Handshake, Handshake>(EchoMethod, new Handshake(1));
        Pump(serverConn, server);
        await gate.Task;

        Task<RpcResult<Handshake>> second = client.InvokeAsync<Handshake, Handshake>(EchoMethod, new Handshake(2));
        Pump(serverConn, server);
        Pump(clientConn, client);

        RpcResult<Handshake> result = await second;
        Assert.False(result.IsSuccess);
        Assert.Equal((int)RpcErrorCode.Busy, result.ErrorCode);
        Assert.False(first.IsCompleted);
    }

    [Fact]
    public async Task CancellationBeforeDispatchSkipsHandler()
    {
        (InMemoryConnection clientConn, InMemoryConnection serverConn) = InMemoryConnection.CreatePair();
        var client = new RpcPeer(clientConn);
        var server = new RpcPeer(serverConn);
        var firstGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int secondMethodCalls = 0;
        var thirdGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.DispatchHook = (context, payload) =>
        {
            if (context.Method == 1)
            {
                firstGate.TrySetResult();
                return new ValueTask(blocker.Task);
            }

            if (context.Method == 2)
            {
                secondMethodCalls++;
                return default;
            }

            thirdGate.TrySetResult();
            return EchoHook(context, payload);
        };

        Task<RpcResult<Handshake>> first = client.InvokeAsync<Handshake, Handshake>(1, new Handshake(1));
        Pump(serverConn, server);
        await firstGate.Task;

        using var cancellation = new CancellationTokenSource();
        Task<RpcResult<Handshake>> second = client.InvokeAsync<Handshake, Handshake>(
            2, new Handshake(2), cancellationToken: cancellation.Token);
        Pump(serverConn, server);
        cancellation.Cancel();
        Pump(serverConn, server);

        // Unblock the worker. It skips the cancelled request before method 3.
        blocker.SetResult();
        Task<RpcResult<Handshake>> third = client.InvokeAsync<Handshake, Handshake>(3, new Handshake(3));
        Pump(serverConn, server);
        await thirdGate.Task;

        Assert.Equal(0, secondMethodCalls);
        RpcResult<Handshake> secondResult = await second;
        Assert.Equal((int)RpcErrorCode.Cancelled, secondResult.ErrorCode);
        Assert.False(first.IsCompleted);
    }

    [Fact]
    public async Task CancellationDuringHandlerDropsResponse()
    {
        (InMemoryConnection clientConn, InMemoryConnection serverConn) = InMemoryConnection.CreatePair();
        var client = new RpcPeer(clientConn);
        var server = new RpcPeer(serverConn);
        RpcContext captured = default;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.DispatchHook = (context, payload) =>
        {
            captured = context;
            gate.TrySetResult();
            return default;
        };

        Task<RpcResult<Handshake>> call = client.InvokeAsync<Handshake, Handshake>(EchoMethod, new Handshake(1));
        Pump(serverConn, server);
        await gate.Task;

        var cancelEnvelope = new Envelope(1, (byte)RpcMessageKind.Cancellation, 1UL, 0UL, null, null);
        RpcTestHelpers.Deliver(server, in cancelEnvelope);

        captured.Respond(RpcResult<Handshake>.Success(new Handshake(9)));
        Pump(clientConn, client);

        Assert.DoesNotContain(
            serverConn.SentFrames,
            frame => Envelope.Deserialize((ReadOnlySpan<byte>)frame.AsSpan(4)).Kind == (byte)RpcMessageKind.Response);
    }

    [Fact]
    public async Task LateResponseAfterCancellationIsIgnored()
    {
        (InMemoryConnection clientConn, InMemoryConnection serverConn) = InMemoryConnection.CreatePair();
        var client = new RpcPeer(clientConn);
        var server = new RpcPeer(serverConn);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.DispatchHook = (context, payload) =>
        {
            ValueTask response = EchoHook(context, payload);
            gate.TrySetResult();
            return response;
        };

        using var cancellation = new CancellationTokenSource();
        Task<RpcResult<Handshake>> call = client.InvokeAsync<Handshake, Handshake>(
            EchoMethod, new Handshake(42), cancellationToken: cancellation.Token);
        Pump(serverConn, server);
        await gate.Task;
        cancellation.Cancel();

        // The response was in flight before the cancellation. The client drops it.
        Pump(clientConn, client);

        RpcResult<Handshake> result = await call;
        Assert.Equal((int)RpcErrorCode.Cancelled, result.ErrorCode);
    }

    [Fact]
    public async Task DisconnectFaultsPendingCalls()
    {
        (InMemoryConnection clientConn, _) = InMemoryConnection.CreatePair();
        var client = new RpcPeer(clientConn);
        Task<RpcResult<Handshake>> first = client.InvokeAsync<Handshake, Handshake>(EchoMethod, new Handshake(1));
        Task<RpcResult<Handshake>> second = client.InvokeAsync<Handshake, Handshake>(EchoMethod, new Handshake(2));

        clientConn.Disconnect();

        RpcResult<Handshake> firstResult = await first;
        RpcResult<Handshake> secondResult = await second;
        Assert.Equal((int)RpcErrorCode.Unavailable, firstResult.ErrorCode);
        Assert.Equal((int)RpcErrorCode.Unavailable, secondResult.ErrorCode);

        Task<RpcResult<Handshake>> after = client.InvokeAsync<Handshake, Handshake>(EchoMethod, new Handshake(3));
        Assert.Equal((int)RpcErrorCode.Unavailable, (await after).ErrorCode);
        Assert.False(client.Notify(EchoMethod));
    }

    [Fact]
    public async Task VersionMismatchFaultsPendingCalls()
    {
        (InMemoryConnection clientConn, _) = InMemoryConnection.CreatePair();
        var client = new RpcPeer(clientConn);
        Task<RpcResult<Handshake>> call = client.InvokeAsync<Handshake, Handshake>(EchoMethod, new Handshake(1));

        var envelope = new Envelope(2, (byte)RpcMessageKind.Request, 1UL, EchoMethod, null, null);
        RpcTestHelpers.Deliver(client, in envelope);

        RpcResult<Handshake> result = await call;
        Assert.Equal((int)RpcErrorCode.Unavailable, result.ErrorCode);
    }

    [Fact]
    public async Task UnknownKindIsDroppedAndPeerStaysUsable()
    {
        (InMemoryConnection clientConn, InMemoryConnection serverConn) = InMemoryConnection.CreatePair();
        var client = new RpcPeer(clientConn);
        var server = new RpcPeer(serverConn);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.DispatchHook = (context, payload) =>
        {
            ValueTask response = EchoHook(context, payload);
            gate.TrySetResult();
            return response;
        };

        var envelope = new Envelope(1, 9, 1UL, EchoMethod, null, null);
        RpcTestHelpers.Deliver(client, in envelope);

        Task<RpcResult<Handshake>> call = client.InvokeAsync<Handshake, Handshake>(EchoMethod, new Handshake(7));
        Pump(serverConn, server);
        await gate.Task;
        Pump(clientConn, client);

        Assert.True((await call).IsSuccess);
    }

    [Fact]
    public async Task PeerDropsFrameAboveMaxFrameBytes()
    {
        var options = new RpcOptions { MaxFrameBytes = 64 };
        var peer = new RpcPeer(new InMemoryConnection(), options);

        int calls = await CountDispatchesAfterMalformedInput(peer, state => state.HandleFrame(new byte[65]));

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task VersionMismatchClosesTheConnection()
    {
        var connection = new InMemoryConnection();
        var peer = new RpcPeer(connection);
        Task<RpcResult<Handshake>> call =
            peer.InvokeAsync<Handshake, Handshake>(EchoMethod, new Handshake(1));

        var envelope = new Envelope(2, (byte)RpcMessageKind.Notification, 0UL, EchoMethod, null, null);
        RpcTestHelpers.Deliver(peer, in envelope);

        Assert.False(connection.IsConnected);
        RpcResult<Handshake> result = await call;
        Assert.Equal((int)RpcErrorCode.Unavailable, result.ErrorCode);
    }

    [Fact]
    public async Task PeerAcceptsAFrameAtTheCapAndDropsOneAboveIt()
    {
        // A null-payload notification envelope is 26 bytes, so the frame is 30.
        var options = new RpcOptions { MaxFrameBytes = 30 };
        var peer = new RpcPeer(new InMemoryConnection(), options);
        var atCap = new Envelope(1, (byte)RpcMessageKind.Notification, 0UL, ProbeMethod, null, null);

        int atCapCalls = await CountDispatchesAfterMalformedInput(
            peer, state => RpcTestHelpers.Deliver(state, in atCap));
        int aboveCapCalls = await CountDispatchesAfterMalformedInput(
            peer, state => state.HandleFrame(new byte[31]));

        Assert.Equal(2, atCapCalls);
        Assert.Equal(1, aboveCapCalls);
    }

    [Fact]
    public async Task PeerDropsFrameShorterThanTheLengthPrefix()
    {
        var peer = new RpcPeer(new InMemoryConnection());

        int calls = await CountDispatchesAfterMalformedInput(peer, state => state.HandleFrame([1, 2]));

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task PeerDropsFrameWithMismatchedLength()
    {
        var peer = new RpcPeer(new InMemoryConnection());

        // The prefix claims three bytes. One byte follows.
        int calls = await CountDispatchesAfterMalformedInput(peer, state => state.HandleFrame([3, 0, 0, 0, 9]));

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task PeerDropsUndecodableEnvelope()
    {
        var peer = new RpcPeer(new InMemoryConnection());

        // Field id 0 announces a one-byte field value that never arrives.
        int calls = await CountDispatchesAfterMalformedInput(peer, state => state.HandleFrame([2, 0, 0, 0, 0, 0]));

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task RequestWithoutIdIsDropped()
    {
        var connection = new InMemoryConnection();
        var peer = new RpcPeer(connection);
        int hookCalls = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        peer.DispatchHook = (context, payload) =>
        {
            hookCalls++;
            gate.TrySetResult();
            return default;
        };

        var invalid = new Envelope(1, (byte)RpcMessageKind.Request, 0UL, EchoMethod, null, null);
        RpcTestHelpers.Deliver(peer, in invalid);

        var valid = new Envelope(1, (byte)RpcMessageKind.Request, 1UL, EchoMethod, null, null);
        RpcTestHelpers.Deliver(peer, in valid);
        await gate.Task;

        Assert.Equal(1, hookCalls);
    }

    [Fact]
    public async Task PeerDropsRequestWithZeroMethod()
    {
        var peer = new RpcPeer(new InMemoryConnection());

        int calls = await CountDispatchesAfterMalformedInput(peer, state => RpcTestHelpers.Deliver(
            state,
            new Envelope(1, (byte)RpcMessageKind.Request, 1UL, 0UL, null, null)));

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task PeerDropsRequestWithError()
    {
        var peer = new RpcPeer(new InMemoryConnection());
        var error = new RpcError((int)RpcErrorCode.InternalError, "nonsense");

        int calls = await CountDispatchesAfterMalformedInput(peer, state => RpcTestHelpers.Deliver(
            state,
            new Envelope(1, (byte)RpcMessageKind.Request, 1UL, EchoMethod, null, error)));

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task PeerDropsResponseWithMethod()
    {
        (InMemoryConnection clientConn, _) = InMemoryConnection.CreatePair();
        var client = new RpcPeer(clientConn);
        Task<RpcResult<Handshake>> call = client.InvokeAsync<Handshake, Handshake>(EchoMethod, new Handshake(1));

        var response = new Envelope(
            1, (byte)RpcMessageKind.Response, 1UL, EchoMethod, new Handshake(2).Serialize(), null);
        RpcTestHelpers.Deliver(client, in response);

        Assert.False(call.IsCompleted);
    }

    [Fact]
    public async Task PeerDropsNotificationWithZeroMethod()
    {
        var peer = new RpcPeer(new InMemoryConnection());

        int calls = await CountDispatchesAfterMalformedInput(peer, state => RpcTestHelpers.Deliver(
            state,
            new Envelope(1, (byte)RpcMessageKind.Notification, 0UL, 0UL, null, null)));

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task PeerDropsNotificationWithError()
    {
        var peer = new RpcPeer(new InMemoryConnection());
        var error = new RpcError((int)RpcErrorCode.InternalError, "nonsense");

        int calls = await CountDispatchesAfterMalformedInput(peer, state => RpcTestHelpers.Deliver(
            state,
            new Envelope(1, (byte)RpcMessageKind.Notification, 0UL, EchoMethod, null, error)));

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task PeerDropsChunkWithoutMethod()
    {
        var connection = new InMemoryConnection();
        var peer = new RpcPeer(connection);
        var sink = new RecordingSink();
        Task<RpcResult<Handshake>> call = peer.InvokeStreamAsync<Handshake, Handshake, Handshake>(
            EchoMethod, new Handshake(1), sink);

        var chunk = new Envelope(
            1, (byte)RpcMessageKind.Notification, 1UL, 0UL, new Handshake(2).Serialize(), null);
        RpcTestHelpers.Deliver(peer, in chunk);

        Assert.Empty(sink.Chunks);
        Assert.False(call.IsCompleted);
    }

    [Fact]
    public async Task PeerDropsChunkWithError()
    {
        var connection = new InMemoryConnection();
        var peer = new RpcPeer(connection);
        var sink = new RecordingSink();
        Task<RpcResult<Handshake>> call = peer.InvokeStreamAsync<Handshake, Handshake, Handshake>(
            EchoMethod, new Handshake(1), sink);
        var error = new RpcError((int)RpcErrorCode.InternalError, "nonsense");

        var chunk = new Envelope(
            1, (byte)RpcMessageKind.Notification, 1UL, EchoMethod, new Handshake(2).Serialize(), error);
        RpcTestHelpers.Deliver(peer, in chunk);

        Assert.Empty(sink.Chunks);
        Assert.False(call.IsCompleted);
    }

    [Fact]
    public async Task PeerDropsChunkForNonStreamCall()
    {
        (InMemoryConnection clientConn, _) = InMemoryConnection.CreatePair();
        var client = new RpcPeer(clientConn);
        Task<RpcResult<Handshake>> call = client.InvokeAsync<Handshake, Handshake>(EchoMethod, new Handshake(1));

        var chunk = new Envelope(
            1, (byte)RpcMessageKind.Notification, 1UL, EchoMethod, new Handshake(2).Serialize(), null);
        RpcTestHelpers.Deliver(client, in chunk);

        // The call stays pending. A later response still completes it.
        Assert.False(call.IsCompleted);
        var response = new Envelope(1, (byte)RpcMessageKind.Response, 1UL, 0UL, new Handshake(3).Serialize(), null);
        RpcTestHelpers.Deliver(client, in response);
        RpcResult<Handshake> result = await call;
        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.ProtocolVersion);
    }

    [Fact]
    public async Task PeerDropsCancellationWithZeroId()
    {
        var cancellation = new Envelope(1, (byte)RpcMessageKind.Cancellation, 0UL, 0UL, null, null);

        await AssertCancellationIsDropped(cancellation);
    }

    [Fact]
    public async Task PeerDropsCancellationWithMethod()
    {
        var cancellation = new Envelope(1, (byte)RpcMessageKind.Cancellation, 1UL, EchoMethod, null, null);

        await AssertCancellationIsDropped(cancellation);
    }

    [Fact]
    public async Task PeerDropsCancellationWithPayload()
    {
        var cancellation = new Envelope(
            1, (byte)RpcMessageKind.Cancellation, 1UL, 0UL, new Handshake(2).Serialize(), null);

        await AssertCancellationIsDropped(cancellation);
    }

    [Fact]
    public async Task PeerDropsCancellationWithError()
    {
        var error = new RpcError((int)RpcErrorCode.InternalError, "nonsense");
        var cancellation = new Envelope(1, (byte)RpcMessageKind.Cancellation, 1UL, 0UL, null, error);

        await AssertCancellationIsDropped(cancellation);
    }

    [Fact]
    public async Task PeerDropsCancellationWithUnknownId()
    {
        var cancellation = new Envelope(1, (byte)RpcMessageKind.Cancellation, 2UL, 0UL, null, null);

        await AssertCancellationIsDropped(cancellation);
    }

    [Fact]
    public async Task ContextFailSendsErrorResponse()
    {
        (_, InMemoryConnection serverConn) = InMemoryConnection.CreatePair();
        var server = new RpcPeer(serverConn);
        RpcContext captured = await CaptureInboundContextAsync(server);

        captured.Fail(RpcErrorCode.PermissionDenied, "denied");

        Assert.Single(serverConn.SentFrames);
        Envelope response = Envelope.Deserialize((ReadOnlySpan<byte>)serverConn.SentFrames[0].AsSpan(4));
        Assert.Equal((byte)RpcMessageKind.Response, response.Kind);
        Assert.NotNull(response.Error);
        Assert.Equal((int)RpcErrorCode.PermissionDenied, response.Error.Value.Code);
        Assert.Equal("denied", response.Error.Value.Message);
    }

    [Fact]
    public async Task ContextFailAfterCancellationIsDropped()
    {
        (_, InMemoryConnection serverConn) = InMemoryConnection.CreatePair();
        var server = new RpcPeer(serverConn);
        RpcContext captured = await CaptureInboundContextAsync(server);

        var cancellation = new Envelope(1, (byte)RpcMessageKind.Cancellation, 1UL, 0UL, null, null);
        RpcTestHelpers.Deliver(server, in cancellation);
        captured.Fail(RpcErrorCode.PermissionDenied, "denied");

        Assert.Empty(serverConn.SentFrames);
    }

    [Fact]
    public async Task ContextFailAfterRespondAsserts()
    {
        (_, InMemoryConnection serverConn) = InMemoryConnection.CreatePair();
        var server = new RpcPeer(serverConn);
        RpcContext captured = await CaptureInboundContextAsync(server);
        captured.Respond(RpcResult<Handshake>.Success(new Handshake(9)));

        Assert.Throws<InvalidOperationException>(
            () => captured.Fail(RpcErrorCode.PermissionDenied, "denied"));
    }

    [Fact]
    public async Task RespondAfterFaultIsDropped()
    {
        (_, InMemoryConnection serverConn) = InMemoryConnection.CreatePair();
        var server = new RpcPeer(serverConn);
        RpcContext captured = await CaptureInboundContextAsync(server);

        serverConn.Disconnect();
        captured.Respond(RpcResult<Handshake>.Success(new Handshake(9)));

        Assert.Empty(serverConn.SentFrames);
    }

    [Fact]
    public async Task CancelledSinkStreamCompletesWithCancelled()
    {
        var connection = new InMemoryConnection();
        var peer = new RpcPeer(connection);
        var sink = new RecordingSink();
        using var cancellation = new CancellationTokenSource();

        Task<RpcResult<Handshake>> call = peer.InvokeStreamAsync<Handshake, Handshake, Handshake>(
            EchoMethod, new Handshake(1), sink, cancellationToken: cancellation.Token);
        cancellation.Cancel();

        Assert.Equal((int)RpcErrorCode.Cancelled, (await call).ErrorCode);
    }

    [Fact]
    public async Task ResponseWithMalformedPayloadFailsWithInvalidRequest()
    {
        (InMemoryConnection clientConn, _) = InMemoryConnection.CreatePair();
        var client = new RpcPeer(clientConn);
        Task<RpcResult<Handshake>> call = client.InvokeAsync<Handshake, Handshake>(EchoMethod, new Handshake(1));

        var response = new Envelope(1, (byte)RpcMessageKind.Response, 1UL, 0UL, [0, 0], null);
        RpcTestHelpers.Deliver(client, in response);

        RpcResult<Handshake> result = await call;
        Assert.False(result.IsSuccess);
        Assert.Equal((int)RpcErrorCode.InvalidRequest, result.ErrorCode);
    }

    [Fact]
    public async Task ResponseWithNullPayloadCompletesWithDefault()
    {
        (InMemoryConnection clientConn, _) = InMemoryConnection.CreatePair();
        var client = new RpcPeer(clientConn);
        Task<RpcResult<Handshake>> call = client.InvokeAsync<Handshake, Handshake>(EchoMethod, new Handshake(1));

        var response = new Envelope(1, (byte)RpcMessageKind.Response, 1UL, 0UL, null, null);
        RpcTestHelpers.Deliver(client, in response);

        RpcResult<Handshake> result = await call;
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.ProtocolVersion);
    }

    [Fact]
    public async Task ChunkWithMalformedPayloadFailsWithInvalidRequest()
    {
        (InMemoryConnection clientConn, _) = InMemoryConnection.CreatePair();
        var client = new RpcPeer(clientConn);
        var sink = new RecordingSink();
        Task<RpcResult<Handshake>> call = client.InvokeStreamAsync<Handshake, Handshake, Handshake>(
            EchoMethod, new Handshake(1), sink);

        var chunk = new Envelope(
            1, (byte)RpcMessageKind.Notification, 1UL, EchoMethod, [0, 0], null);
        RpcTestHelpers.Deliver(client, in chunk);

        RpcResult<Handshake> result = await call;
        Assert.Equal((int)RpcErrorCode.InvalidRequest, result.ErrorCode);
        Assert.Empty(sink.Chunks);
    }

    [Fact]
    public async Task ChunkResetsIdleDeadline()
    {
        long now = 0;
        var options = new RpcOptions { TimestampNanos = () => now };
        var connection = new InMemoryConnection();
        var peer = new RpcPeer(connection, options);
        var sink = new RecordingSink();

        Task<RpcResult<Handshake>> call = peer.InvokeStreamAsync<Handshake, Handshake, Handshake>(
            EchoMethod, new Handshake(1), sink, TimeSpan.FromSeconds(5));

        // A chunk at four seconds resets the idle deadline to nine seconds.
        now = 4_000_000_000;
        var chunk = new Envelope(
            1, (byte)RpcMessageKind.Notification, 1UL, EchoMethod, new Handshake(2).Serialize(), null);
        RpcTestHelpers.Deliver(peer, in chunk);

        now = 6_000_000_000;
        peer.CheckTimeouts(now);
        Assert.False(call.IsCompleted);
        Assert.Single(sink.Chunks);

        now = 9_000_000_000;
        peer.CheckTimeouts(now);
        RpcResult<Handshake> result = await call;
        Assert.Equal((int)RpcErrorCode.RequestTimeout, result.ErrorCode);
    }

    [Fact]
    public async Task StreamDeliversChunksInOrderThenResponse()
    {
        (InMemoryConnection clientConn, InMemoryConnection serverConn) = InMemoryConnection.CreatePair();
        var client = new RpcPeer(clientConn);
        var server = new RpcPeer(serverConn);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.DispatchHook = (context, payload) =>
        {
            context.Stream(new Handshake(10));
            context.Stream(new Handshake(20));
            context.Respond(RpcResult<Handshake>.Success(new Handshake(30)));
            gate.TrySetResult();
            return default;
        };

        var sink = new RecordingSink();
        Task<RpcResult<Handshake>> call = client.InvokeStreamAsync<Handshake, Handshake, Handshake>(
            EchoMethod, new Handshake(1), sink);

        Pump(serverConn, server);
        await gate.Task;
        Pump(clientConn, client);

        RpcResult<Handshake> result = await call;
        Assert.True(result.IsSuccess);
        Assert.Equal(30, result.Value.ProtocolVersion);
        Assert.Equal([10, 20], sink.Chunks.Select(chunk => chunk.ProtocolVersion).ToArray());
    }

    [Fact]
    public async Task ChunksAfterCompletionAreDropped()
    {
        (InMemoryConnection clientConn, InMemoryConnection serverConn) = InMemoryConnection.CreatePair();
        var client = new RpcPeer(clientConn);
        var server = new RpcPeer(serverConn);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.DispatchHook = (context, payload) =>
        {
            ValueTask response = EchoHook(context, payload);
            gate.TrySetResult();
            return response;
        };

        var sink = new RecordingSink();
        Task<RpcResult<Handshake>> call = client.InvokeStreamAsync<Handshake, Handshake, Handshake>(
            EchoMethod, new Handshake(1), sink);
        Pump(serverConn, server);
        await gate.Task;
        Pump(clientConn, client);
        Assert.True((await call).IsSuccess);

        var chunk = new Envelope(
            1, (byte)RpcMessageKind.Notification, 1UL, EchoMethod, new Handshake(2).Serialize(), null);
        RpcTestHelpers.Deliver(client, in chunk);

        Assert.Empty(sink.Chunks);
    }

    [Fact]
    public async Task NotificationDispatchesToHook()
    {
        var connection = new InMemoryConnection();
        var peer = new RpcPeer(connection);
        RpcContext captured = default;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        peer.DispatchHook = (context, payload) =>
        {
            captured = context;
            gate.TrySetResult();
            return default;
        };

        var notification = new Envelope(
            1, (byte)RpcMessageKind.Notification, 0UL, EchoMethod, new Handshake(4).Serialize(), null);
        RpcTestHelpers.Deliver(peer, in notification);
        await gate.Task;

        Assert.Equal(0UL, captured.RequestId);
        Assert.Equal(EchoMethod, captured.Method);
    }

    [Fact]
    public async Task FailedSendCompletesCallWithUnavailable()
    {
        var connection = new InMemoryConnection { IsConnectedValue = false };
        var peer = new RpcPeer(connection);

        RpcResult<Handshake> result = await peer.InvokeAsync<Handshake, Handshake>(
            EchoMethod, new Handshake(1));

        Assert.False(result.IsSuccess);
        Assert.Equal((int)RpcErrorCode.Unavailable, result.ErrorCode);
    }

    [Fact]
    public void NotifySendsStandaloneNotificationAndReportsRejection()
    {
        var connection = new InMemoryConnection();
        var peer = new RpcPeer(connection);

        bool sent = peer.Notify(EchoMethod, new Handshake(5));

        Assert.True(sent);
        Envelope frame = DecodeFrames(connection.SentFrames)[0];
        Assert.Equal((byte)RpcMessageKind.Notification, frame.Kind);
        Assert.Equal(0UL, frame.Id);
        Assert.Equal(EchoMethod, frame.Method);

        connection.Disconnect();
        Assert.False(peer.Notify(EchoMethod, new Handshake(6)));
        Assert.False(peer.Notify(EchoMethod));
    }

    [Fact]
    public void NotifyWithoutPayloadSendsStandaloneNotification()
    {
        var connection = new InMemoryConnection();
        var peer = new RpcPeer(connection);

        bool sent = peer.Notify(EchoMethod);

        Assert.True(sent);
        Envelope frame = DecodeFrames(connection.SentFrames)[0];
        Assert.Equal((byte)RpcMessageKind.Notification, frame.Kind);
        Assert.Equal(0UL, frame.Id);
        Assert.Equal(EchoMethod, frame.Method);
        Assert.Null(frame.Payload);
    }

    [Fact]
    public async Task DuplicateRequestIdIsDropped()
    {
        (InMemoryConnection clientConn, InMemoryConnection serverConn) = InMemoryConnection.CreatePair();
        var client = new RpcPeer(clientConn);
        var server = new RpcPeer(serverConn);
        int hookCalls = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.DispatchHook = (context, payload) =>
        {
            hookCalls++;
            gate.TrySetResult();
            return default;
        };

        Task<RpcResult<Handshake>> call = client.InvokeAsync<Handshake, Handshake>(EchoMethod, new Handshake(1));
        Pump(serverConn, server);
        await gate.Task;

        byte[] requestFrame = clientConn.SentFrames[0];
        server.HandleFrame(requestFrame);

        Assert.Equal(1, hookCalls);
        Assert.False(call.IsCompleted);
    }

    [Fact]
    public async Task PollChecksTimeouts()
    {
        long now = 0;
        var options = new RpcOptions { TimestampNanos = () => now };
        var peer = new RpcPeer(new InMemoryConnection(), options);

        Task<RpcResult<Handshake>> call = peer.InvokeAsync<Handshake, Handshake>(
            EchoMethod, new Handshake(1), TimeSpan.FromSeconds(5));

        now = 5_000_000_000;
        peer.Poll();

        Assert.Equal((int)RpcErrorCode.RequestTimeout, (await call).ErrorCode);
    }

    [Fact]
    public async Task PumpIsNotBlockedByARunningHandler()
    {
        (InMemoryConnection clientConn, InMemoryConnection serverConn) = InMemoryConnection.CreatePair();
        var client = new RpcPeer(clientConn);
        var server = new RpcPeer(serverConn);
        var firstGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.DispatchHook = (context, payload) =>
        {
            firstGate.TrySetResult();
            return new ValueTask(blocker.Task);
        };

        var sink = new RecordingSink();
        Task<RpcResult<Handshake>> stream = client.InvokeStreamAsync<Handshake, Handshake, Handshake>(
            EchoMethod, new Handshake(1), sink);

        Pump(serverConn, server);
        await firstGate.Task;

        // The worker awaits the blocked handler. A chunk still reaches the sink.
        var chunk = new Envelope(
            1, (byte)RpcMessageKind.Notification, 1UL, EchoMethod, new Handshake(2).Serialize(), null);
        RpcTestHelpers.Deliver(client, in chunk);
        Assert.Single(sink.Chunks);

        blocker.SetResult();
        Assert.False(stream.IsCompleted);
    }

    [Fact]
    public async Task InvokeAsyncRejectsMethodZero()
    {
        var peer = new RpcPeer(new InMemoryConnection());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => peer.InvokeAsync<Handshake, Handshake>(0, new Handshake(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => peer.Notify(0, new Handshake(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => peer.Notify(0));
    }

    [Fact]
    public async Task InvokeAsyncRejectsNegativeTimeout()
    {
        var peer = new RpcPeer(new InMemoryConnection());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => peer.InvokeAsync<Handshake, Handshake>(
                EchoMethod, new Handshake(1), TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void PeerRejectsInvalidOptions()
    {
        var connection = new InMemoryConnection();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RpcPeer(connection, new RpcOptions { MaxPendingCalls = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RpcPeer(connection, new RpcOptions { MaxFrameBytes = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RpcPeer(connection, new RpcOptions { MaxBufferedChunks = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RpcPeer(connection, new RpcOptions { Timeout = TimeSpan.FromSeconds(-1) }));
        Assert.Throws<ArgumentNullException>(() => new RpcPeer(null!));
    }

    [Fact]
    public async Task InvokeStreamAsyncRejectsMethodZero()
    {
        var peer = new RpcPeer(new InMemoryConnection());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => peer.InvokeStreamAsync<Handshake, Handshake, Handshake>(
                0, new Handshake(1), new RecordingSink()));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => peer.InvokeStreamAsync<Handshake, Handshake, Handshake>(0, new Handshake(1)));
    }

    [Fact]
    public async Task InvokeStreamAsyncRejectsNullSink()
    {
        var peer = new RpcPeer(new InMemoryConnection());

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => peer.InvokeStreamAsync<Handshake, Handshake, Handshake>(
                EchoMethod, new Handshake(1), (IChunkSink<Handshake>)null!));
    }

    [Fact]
    public async Task OversizedRequestFrameFailsEveryInvokeOverload()
    {
        var options = new RpcOptions { MaxFrameBytes = 1 };
        var connection = new InMemoryConnection();
        var peer = new RpcPeer(connection, options);

        RpcResult<Handshake> call = await peer.InvokeAsync<Handshake, Handshake>(
            EchoMethod, new Handshake(1));
        RpcResult<Handshake> sinkStream = await peer.InvokeStreamAsync<Handshake, Handshake, Handshake>(
            EchoMethod, new Handshake(1), new RecordingSink());
        RpcStreamSession<Handshake, Handshake> session =
            peer.InvokeStreamAsync<Handshake, Handshake, Handshake>(EchoMethod, new Handshake(1));

        Assert.Equal((int)RpcErrorCode.InvalidRequest, call.ErrorCode);
        Assert.Equal((int)RpcErrorCode.InvalidRequest, sinkStream.ErrorCode);
        Assert.Equal((int)RpcErrorCode.InvalidRequest, (await session.Response).ErrorCode);
        Assert.Empty(connection.SentFrames);
    }

    [Fact]
    public async Task StreamInvokesAfterFaultFailWithUnavailable()
    {
        var connection = new InMemoryConnection();
        var peer = new RpcPeer(connection);
        connection.Disconnect();

        RpcResult<Handshake> sinkStream = await peer.InvokeStreamAsync<Handshake, Handshake, Handshake>(
            EchoMethod, new Handshake(1), new RecordingSink());
        RpcStreamSession<Handshake, Handshake> session =
            peer.InvokeStreamAsync<Handshake, Handshake, Handshake>(EchoMethod, new Handshake(1));

        Assert.Equal((int)RpcErrorCode.Unavailable, sinkStream.ErrorCode);
        Assert.Equal((int)RpcErrorCode.Unavailable, (await session.Response).ErrorCode);
    }

    [Fact]
    public async Task ZeroTimeoutStreamReceivesChunksAndStaysAlive()
    {
        long now = 0;
        var options = new RpcOptions { TimestampNanos = () => now };
        var connection = new InMemoryConnection();
        var peer = new RpcPeer(connection, options);
        var sink = new RecordingSink();
        Task<RpcResult<Handshake>> call = peer.InvokeStreamAsync<Handshake, Handshake, Handshake>(
            EchoMethod, new Handshake(1), sink, TimeSpan.Zero);

        now = 100_000_000_000;
        var chunk = new Envelope(
            1, (byte)RpcMessageKind.Notification, 1UL, EchoMethod, new Handshake(2).Serialize(), null);
        RpcTestHelpers.Deliver(peer, in chunk);
        peer.CheckTimeouts(now);

        Assert.Single(sink.Chunks);
        Assert.False(call.IsCompleted);
    }

    [Fact]
    public async Task InvokeAsyncRejectsTimeoutAboveTheSupportedRange()
    {
        var peer = new RpcPeer(new InMemoryConnection());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => peer.InvokeAsync<Handshake, Handshake>(
                EchoMethod, new Handshake(1), TimeSpan.MaxValue));
    }

    [Fact]
    public async Task DeadlineOverflowDisablesTheTimeout()
    {
        long now = long.MaxValue - 100;
        var options = new RpcOptions { TimestampNanos = () => now };
        var peer = new RpcPeer(new InMemoryConnection(), options);

        Task<RpcResult<Handshake>> call = peer.InvokeAsync<Handshake, Handshake>(
            EchoMethod, new Handshake(1), TimeSpan.FromSeconds(5));
        peer.CheckTimeouts(long.MaxValue - 1);

        Assert.False(call.IsCompleted);
    }

    [Fact]
    public async Task PeerWithoutDispatchHookAnswersMethodNotFound()
    {
        (InMemoryConnection clientConn, InMemoryConnection serverConn) = InMemoryConnection.CreatePair();
        var client = new RpcPeer(clientConn);
        var server = new RpcPeer(serverConn);
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        clientConn.FrameDelivered += () => delivered.TrySetResult();

        Task<RpcResult<Handshake>> call = client.InvokeAsync<Handshake, Handshake>(EchoMethod, new Handshake(1));
        Pump(serverConn, server);
        await delivered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Pump(clientConn, client);

        RpcResult<Handshake> result = await call;
        Assert.False(result.IsSuccess);
        Assert.Equal((int)RpcErrorCode.MethodNotFound, result.ErrorCode);
    }

    [Fact]
    public async Task PeerAnswersInternalErrorWhenTheDispatchHookThrows()
    {
        (InMemoryConnection clientConn, InMemoryConnection serverConn) = InMemoryConnection.CreatePair();
        var client = new RpcPeer(clientConn);
        var server = new RpcPeer(serverConn);
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        clientConn.FrameDelivered += () => delivered.TrySetResult();
        server.DispatchHook = (context, payload) => throw new InvalidOperationException("boom");

        Task<RpcResult<Handshake>> call = client.InvokeAsync<Handshake, Handshake>(EchoMethod, new Handshake(1));
        Pump(serverConn, server);
        await delivered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Pump(clientConn, client);

        RpcResult<Handshake> result = await call;
        Assert.Equal((int)RpcErrorCode.InternalError, result.ErrorCode);
    }

    // Delivers malformed input, then a probe notification. Dispatch is serial,
    // so the probe is dispatched only after any wrongly accepted input. The
    // call count is one when the input was dropped.
    private static async Task<int> CountDispatchesAfterMalformedInput(
        RpcPeer peer, Action<RpcPeer> deliverMalformedInput)
    {
        int calls = 0;
        var probeGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        peer.DispatchHook = (context, payload) =>
        {
            calls++;
            if (context.Method == ProbeMethod)
            {
                probeGate.TrySetResult();
            }

            return default;
        };

        deliverMalformedInput(peer);
        var probe = new Envelope(1, (byte)RpcMessageKind.Notification, 0UL, ProbeMethod, null, null);
        RpcTestHelpers.Deliver(peer, in probe);
        await probeGate.Task.WaitAsync(TimeSpan.FromSeconds(10));
        return calls;
    }

    // Delivers a request and returns the captured context. The caller answers it.
    private static async Task<RpcContext> CaptureInboundContextAsync(RpcPeer server)
    {
        RpcContext captured = default;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.DispatchHook = (context, payload) =>
        {
            captured = context;
            gate.TrySetResult();
            return default;
        };

        var request = new Envelope(1, (byte)RpcMessageKind.Request, 1UL, EchoMethod, null, null);
        RpcTestHelpers.Deliver(server, in request);
        await gate.Task.WaitAsync(TimeSpan.FromSeconds(10));
        return captured;
    }

    // Delivers a request, then the cancellation, then a response. An invalid
    // cancellation must leave the request claimable, so the response arrives.
    private static async Task AssertCancellationIsDropped(Envelope cancellation)
    {
        (_, InMemoryConnection serverConn) = InMemoryConnection.CreatePair();
        var server = new RpcPeer(serverConn);
        RpcContext captured = await CaptureInboundContextAsync(server);

        RpcTestHelpers.Deliver(server, in cancellation);
        captured.Respond(RpcResult<Handshake>.Success(new Handshake(9)));

        Assert.Single(serverConn.SentFrames);
        Envelope response = Envelope.Deserialize((ReadOnlySpan<byte>)serverConn.SentFrames[0].AsSpan(4));
        Assert.Equal((byte)RpcMessageKind.Response, response.Kind);
    }

    private sealed class RecordingSink : IChunkSink<Handshake>
    {
        public List<Handshake> Chunks { get; } = [];

        public void OnChunk(in Handshake chunk)
        {
            Chunks.Add(chunk);
        }
    }
}
