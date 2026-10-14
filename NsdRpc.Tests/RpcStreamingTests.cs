using System.Buffers.Binary;
using NsdRpc;
using NsdRpc.Wire;

namespace NsdRpc.Tests;

public sealed class RpcStreamingTests
{
    [RpcMethod(400)]
    private sealed class StreamingHandler : IRpcHandler<Handshake, Handshake>
    {
        public List<Handshake> Chunks { get; } = [];

        public Handshake Result { get; set; } = new(40);

        public ValueTask<RpcResult<Handshake>> ExecuteAsync(
            Handshake request, RpcContext context, CancellationToken cancellationToken)
        {
            foreach (Handshake chunk in Chunks)
            {
                context.Stream(chunk);
            }

            context.Respond(RpcResult<Handshake>.Success(Result));
            return new ValueTask<RpcResult<Handshake>>(RpcResult<Handshake>.Success(default));
        }
    }

    [RpcMethod(401)]
    private sealed class GatedStreamHandler : IRpcHandler<Handshake, Handshake>
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Go { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<RpcResult<Handshake>> ExecuteAsync(
            Handshake request, RpcContext context, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Go.Task.ConfigureAwait(false);
            context.Stream(new Handshake(10));
            context.Stream(new Handshake(20));
            context.Respond(RpcResult<Handshake>.Success(new Handshake(30)));
            return RpcResult<Handshake>.Success(default);
        }
    }

    [Fact]
    public async Task StreamSessionDeliversChunksAndResponse()
    {
        var registry = new RpcRegistry();
        var handler = new StreamingHandler();
        handler.Chunks.AddRange([new Handshake(10), new Handshake(20), new Handshake(30)]);
        registry.Add(handler);
        (InMemoryConnection clientConn, InMemoryConnection serverConn) = InMemoryConnection.CreatePair();
        var client = new RpcPeer(clientConn);
        var server = new RpcPeer(serverConn);
        TaskCompletionSource gate = RpcTestHelpers.AttachDispatcher(server, registry);

        RpcStreamSession<Handshake, Handshake> session = client.InvokeStreamAsync<Handshake, Handshake, Handshake>(
            400, new Handshake(1));
        RpcTestHelpers.Pump(serverConn, server);
        await gate.Task;
        RpcTestHelpers.Pump(clientConn, client);

        var received = new List<Handshake>();
        await foreach (Handshake chunk in session.Chunks)
        {
            received.Add(chunk);
        }

        RpcResult<Handshake> result = await session.Response;
        Assert.True(result.IsSuccess);
        Assert.Equal(40, result.Value.ProtocolVersion);
        Assert.Equal([10, 20, 30], received.Select(chunk => chunk.ProtocolVersion).ToArray());
    }

    [Fact]
    public async Task StreamSessionOverflowFaultsWithBusy()
    {
        var registry = new RpcRegistry();
        var handler = new StreamingHandler();
        handler.Chunks.AddRange(
            [new Handshake(1), new Handshake(2), new Handshake(3), new Handshake(4), new Handshake(5)]);
        registry.Add(handler);
        (InMemoryConnection clientConn, InMemoryConnection serverConn) = InMemoryConnection.CreatePair();
        var client = new RpcPeer(clientConn, new RpcOptions { MaxBufferedChunks = 2 });
        var server = new RpcPeer(serverConn);
        TaskCompletionSource gate = RpcTestHelpers.AttachDispatcher(server, registry);

        // The consumer never drains the session, so the queue fills and faults.
        RpcStreamSession<Handshake, Handshake> session = client.InvokeStreamAsync<Handshake, Handshake, Handshake>(
            400, new Handshake(1));
        RpcTestHelpers.Pump(serverConn, server);
        await gate.Task;
        RpcTestHelpers.Pump(clientConn, client);

        RpcResult<Handshake> result = await session.Response;
        Assert.False(result.IsSuccess);
        Assert.Equal((int)RpcErrorCode.Busy, result.ErrorCode);
    }

    [Fact]
    public async Task CancellingAStreamDropsChunksOnBothSides()
    {
        var registry = new RpcRegistry();
        var handler = new GatedStreamHandler();
        registry.Add(handler);
        (InMemoryConnection clientConn, InMemoryConnection serverConn) = InMemoryConnection.CreatePair();
        var client = new RpcPeer(clientConn);
        var server = new RpcPeer(serverConn);
        server.DispatchHook = new RpcDispatcher(registry, server).DispatchAsync;

        using var cancellation = new CancellationTokenSource();
        RpcStreamSession<Handshake, Handshake> session = client.InvokeStreamAsync<Handshake, Handshake, Handshake>(
            401, new Handshake(1), cancellationToken: cancellation.Token);
        RpcTestHelpers.Pump(serverConn, server);
        await handler.Started.Task;

        cancellation.Cancel();
        RpcTestHelpers.Pump(serverConn, server);

        // Let the handler finish. Its chunks and response are dropped at send.
        handler.Go.SetResult();
        RpcTestHelpers.Pump(clientConn, client);

        RpcResult<Handshake> result = await session.Response;
        Assert.Equal((int)RpcErrorCode.Cancelled, result.ErrorCode);
        Assert.DoesNotContain(
            serverConn.SentFrames,
            frame => Envelope.Deserialize((ReadOnlySpan<byte>)frame.AsSpan(4)).Kind
                == (byte)RpcMessageKind.Notification);
    }

    [Fact]
    public async Task StreamingAfterRespondAsserts()
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

        Task<RpcResult<Handshake>> call = client.InvokeAsync<Handshake, Handshake>(400, new Handshake(1));
        RpcTestHelpers.Pump(serverConn, server);
        await gate.Task;

        captured.Respond(RpcResult<Handshake>.Success(new Handshake(1)));
        Assert.Throws<InvalidOperationException>(() => captured.Stream(new Handshake(2)));
    }

    [Fact]
    public async Task RespondingTwiceAsserts()
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

        Task<RpcResult<Handshake>> call = client.InvokeAsync<Handshake, Handshake>(400, new Handshake(1));
        RpcTestHelpers.Pump(serverConn, server);
        await gate.Task;

        captured.Respond(RpcResult<Handshake>.Success(new Handshake(1)));
        Assert.Throws<InvalidOperationException>(
            () => captured.Respond(RpcResult<Handshake>.Success(new Handshake(2))));
    }

    [Fact]
    public void ChunkDispatchAllocatesNothingBeyondTheCodec()
    {
        (InMemoryConnection clientConn, _) = InMemoryConnection.CreatePair();
        var client = new RpcPeer(clientConn);
        var sink = new RecordingSink();
        Task<RpcResult<Handshake>> call = client.InvokeStreamAsync<Handshake, Handshake, Handshake>(
            400, new Handshake(1), sink);

        var chunkEnvelope = new Envelope(
            1, (byte)RpcMessageKind.Notification, 1UL, 400UL, new Handshake(2).Serialize(), null);
        int size = chunkEnvelope.GetSize();
        byte[] chunkFrame = new byte[4 + size];
        BinaryPrimitives.WriteInt32LittleEndian(chunkFrame, size);
        chunkEnvelope.SerializeInto(chunkFrame.AsSpan(4, size));

        // Warm up both paths so first-call JIT and list growth do not count.
        client.HandleFrame(chunkFrame);
        Envelope.Deserialize((ReadOnlySpan<byte>)chunkFrame.AsSpan(4));

        long baselineBefore = GC.GetAllocatedBytesForCurrentThread();
        Envelope.Deserialize((ReadOnlySpan<byte>)chunkFrame.AsSpan(4));
        long baselineDelta = GC.GetAllocatedBytesForCurrentThread() - baselineBefore;

        long before = GC.GetAllocatedBytesForCurrentThread();
        client.HandleFrame(chunkFrame);
        long delta = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(baselineDelta, delta);
        Assert.Equal(2, sink.Chunks.Count);
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
