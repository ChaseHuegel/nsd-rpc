using NsdRpc;
using NsdRpc.Wire;

namespace NsdRpc.Tests;

public sealed class RpcPeerTests
{
    private const ulong EchoMethod = 5;

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
            envelopes.Add(Envelope.Deserialize((ReadOnlySpan<byte>)frame));
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
        client.HandleFrame(envelope.Serialize());

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
        client.HandleFrame(envelope.Serialize());

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
        server.HandleFrame(cancelEnvelope.Serialize());

        captured.Respond(RpcResult<Handshake>.Success(new Handshake(9)));
        Pump(clientConn, client);

        Assert.DoesNotContain(
            serverConn.SentFrames,
            frame => Envelope.Deserialize((ReadOnlySpan<byte>)frame).Kind == (byte)RpcMessageKind.Response);
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
        client.HandleFrame(envelope.Serialize());

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
        client.HandleFrame(envelope.Serialize());

        Task<RpcResult<Handshake>> call = client.InvokeAsync<Handshake, Handshake>(EchoMethod, new Handshake(7));
        Pump(serverConn, server);
        await gate.Task;
        Pump(clientConn, client);

        Assert.True((await call).IsSuccess);
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
        peer.HandleFrame(invalid.Serialize());

        var valid = new Envelope(1, (byte)RpcMessageKind.Request, 1UL, EchoMethod, null, null);
        peer.HandleFrame(valid.Serialize());
        await gate.Task;

        Assert.Equal(1, hookCalls);
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
        peer.HandleFrame(chunk.Serialize());

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
        client.HandleFrame(chunk.Serialize());

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
        peer.HandleFrame(notification.Serialize());
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
        client.HandleFrame(chunk.Serialize());
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

    private sealed class RecordingSink : IChunkSink<Handshake>
    {
        public List<Handshake> Chunks { get; } = [];

        public void OnChunk(in Handshake chunk)
        {
            Chunks.Add(chunk);
        }
    }
}
