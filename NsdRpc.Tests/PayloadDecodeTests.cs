using System.Buffers.Binary;
using System.Diagnostics;
using NsdRpc;
using NsdRpc.Tests.Wire;
using NsdRpc.Wire;

namespace NsdRpc.Tests;

public sealed class PayloadDecodeTests
{
    private const ulong Method = 300;

    // A payload whose reader throws a non-InvalidDataException. It stands in
    // for a generated reader that indexes past a corrupt length.
    private struct ThrowingPayload
    {
        public readonly int GetSize()
        {
            return 4;
        }

        public readonly int SerializeInto(Span<byte> buffer, int start)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(start, 4), 0);
            return 4;
        }

        public static ThrowingPayload Deserialize(ReadOnlySpan<byte> data)
        {
            throw new IndexOutOfRangeException("The reader indexed past the payload.");
        }
    }

    private sealed class ThrowingRequestHandler : IRpcHandler<ThrowingPayload, Handshake>
    {
        public ValueTask<RpcResult<Handshake>> ExecuteAsync(
            ThrowingPayload request, RpcContext context, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("A decode failure must not reach the handler.");
        }
    }

    private sealed class HandshakeEchoHandler : IRpcHandler<Handshake, Handshake>
    {
        public ValueTask<RpcResult<Handshake>> ExecuteAsync(
            Handshake request, RpcContext context, CancellationToken cancellationToken)
        {
            return new ValueTask<RpcResult<Handshake>>(
                RpcResult<Handshake>.Success(new Handshake(request.ProtocolVersion + 1)));
        }
    }

    private sealed class BlobHandler : IRpcHandler<Blob, Handshake>
    {
        public ValueTask<RpcResult<Handshake>> ExecuteAsync(
            Blob request, RpcContext context, CancellationToken cancellationToken)
        {
            return new ValueTask<RpcResult<Handshake>>(
                RpcResult<Handshake>.Success(new Handshake(request.Data?.Length ?? 0)));
        }
    }

    private sealed class ThrowingChunkSink : IChunkSink<ThrowingPayload>
    {
        public List<ThrowingPayload> Chunks { get; } = [];

        public void OnChunk(in ThrowingPayload chunk)
        {
            Chunks.Add(chunk);
        }
    }

    private sealed class CaptureListener : TraceListener
    {
        public string Text { get; private set; } = string.Empty;

        public override void Write(string? message)
        {
            Text += message;
        }

        public override void WriteLine(string? message)
        {
            Text += message;
        }
    }

    [Fact]
    public void TryDeserializeMapsAReaderFailureToFalse()
    {
        IPayloadSerializer<ThrowingPayload> serializer = NsdPayloadSerializer<ThrowingPayload>.Build();

        bool decoded = serializer.TryDeserialize([0, 0], out ThrowingPayload value);

        Assert.False(decoded);
        Assert.Equal(default, value);
    }

    [Fact]
    public async Task RequestDecodeFailureAnswersInvalidParams()
    {
        var registry = new RpcRegistry();
        registry.Add<ThrowingPayload, Handshake>(new ThrowingRequestHandler(), Method);
        var connection = new InMemoryConnection();
        var peer = new RpcPeer(connection);

        var request = new Envelope(
            1, (byte)RpcMessageKind.Request, 7UL, Method, [0, 0], null);
        Envelope response = await DispatchAsync(connection, peer, registry, request);

        Assert.NotNull(response.Error);
        Assert.Equal((int)RpcErrorCode.InvalidParams, response.Error.Value.Code);
        Assert.True(connection.IsConnected);
    }

    [Fact]
    public async Task NotificationDecodeFailureIsDroppedAndLogged()
    {
        var registry = new RpcRegistry();
        registry.Add<ThrowingPayload, Handshake>(new ThrowingRequestHandler(), Method);
        var connection = new InMemoryConnection();
        var peer = new RpcPeer(connection);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = new RpcDispatcher(registry, peer);
        peer.DispatchHook = async (context, payload) =>
        {
            await dispatcher.DispatchAsync(context, payload).ConfigureAwait(false);
            gate.TrySetResult();
        };

        var listener = new CaptureListener();
        SourceLevels previous = RpcLog.Source.Switch.Level;
        RpcLog.Source.Switch.Level = SourceLevels.Warning;
        RpcLog.Source.Listeners.Add(listener);
        try
        {
            var notification = new Envelope(
                1, (byte)RpcMessageKind.Notification, 0UL, Method, [0, 0], null);
            RpcTestHelpers.Deliver(peer, in notification);
            await gate.Task;

            Assert.Contains("Dropped frame: notification payload did not decode", listener.Text);
            Assert.Empty(connection.SentFrames);
            Assert.True(connection.IsConnected);
        }
        finally
        {
            RpcLog.Source.Listeners.Remove(listener);
            RpcLog.Source.Switch.Level = previous;
        }
    }

    [Fact]
    public async Task ChunkDecodeFailureFailsTheStreamCallAndKeepsTheConnection()
    {
        var connection = new InMemoryConnection();
        var peer = new RpcPeer(connection);
        var sink = new ThrowingChunkSink();
        Task<RpcResult<Handshake>> call = peer.InvokeStreamAsync<Handshake, ThrowingPayload, Handshake>(
            Method, new Handshake(1), sink);

        var chunk = new Envelope(
            1, (byte)RpcMessageKind.Notification, 1UL, Method, [0, 0], null);
        RpcTestHelpers.Deliver(peer, in chunk);

        RpcResult<Handshake> result = await call;
        Assert.False(result.IsSuccess);
        Assert.Equal((int)RpcErrorCode.InvalidRequest, result.ErrorCode);
        Assert.Empty(sink.Chunks);
        Assert.True(connection.IsConnected);
    }

    [Fact]
    public async Task TruncatedPayloadAnswersInvalidParams()
    {
        var registry = new RpcRegistry();
        registry.Add<Handshake, Handshake>(new HandshakeEchoHandler(), Method);
        var connection = new InMemoryConnection();
        var peer = new RpcPeer(connection);
        byte[] payload = new Handshake(42).Serialize();
        Assert.True(payload.Length > 3);

        var request = new Envelope(
            1, (byte)RpcMessageKind.Request, 8UL, Method, payload[..3], null);
        Envelope response = await DispatchAsync(connection, peer, registry, request);

        Assert.NotNull(response.Error);
        Assert.Equal((int)RpcErrorCode.InvalidParams, response.Error.Value.Code);
        Assert.True(connection.IsConnected);
    }

    [Fact]
    public async Task PayloadLengthAboveTheBufferAnswersInvalidParams()
    {
        var registry = new RpcRegistry();
        registry.Add<Blob, Handshake>(new BlobHandler(), Method);
        var connection = new InMemoryConnection();
        var peer = new RpcPeer(connection);

        // Field 0, present, length 65,535, and one byte of data. The reader
        // rejects the length before it reads the data.
        byte[] payload = [0, 0, 1, 0xFF, 0xFF, 7];
        var request = new Envelope(
            1, (byte)RpcMessageKind.Request, 9UL, Method, payload, null);
        Envelope response = await DispatchAsync(connection, peer, registry, request);

        Assert.NotNull(response.Error);
        Assert.Equal((int)RpcErrorCode.InvalidParams, response.Error.Value.Code);
        Assert.True(connection.IsConnected);
    }

    private static async Task<Envelope> DispatchAsync(
        InMemoryConnection connection, RpcPeer peer, RpcRegistry registry, Envelope request)
    {
        TaskCompletionSource gate = RpcTestHelpers.AttachDispatcher(peer, registry);
        RpcTestHelpers.Deliver(peer, in request);
        await gate.Task;
        Assert.Single(connection.SentFrames);
        return Envelope.Deserialize((ReadOnlySpan<byte>)connection.SentFrames[0].AsSpan(4));
    }
}
