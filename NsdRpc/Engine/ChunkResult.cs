namespace NsdRpc;

// The outcome of one chunk delivery to a pending call.
internal enum ChunkResult
{
    Consumed,
    NotAStream,
    Failed,
}
