namespace NsdRpc;

internal sealed class PendingStreamCall<TChunk, TResponse> : PendingCall<TResponse>
    where TChunk : struct
    where TResponse : struct
{
    private readonly IChunkSink<TChunk> _sink;
    private readonly IPayloadSerializer<TChunk> _chunkSerializer;

    public PendingStreamCall(
        ulong id,
        long deadlineNanos,
        long timeoutNanos,
        IPayloadSerializer<TResponse> resultSerializer,
        IPayloadSerializer<TChunk> chunkSerializer,
        IChunkSink<TChunk> sink)
        : base(id, deadlineNanos, timeoutNanos, resultSerializer)
    {
        _chunkSerializer = chunkSerializer;
        _sink = sink;
    }

    public override ChunkResult ReceiveChunk(byte[]? payload)
    {
        if (payload == null)
        {
            return ChunkResult.Consumed;
        }

        if (_chunkSerializer.TryDeserialize(payload, out TChunk chunk))
        {
            _sink.OnChunk(in chunk);
            return ChunkResult.Consumed;
        }

        Fail((int)RpcErrorCode.InvalidRequest, "malformed chunk payload");
        return ChunkResult.Failed;
    }
}
