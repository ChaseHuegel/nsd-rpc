namespace NsdRpc;

/// <summary>
/// Consumes one chunk of a stream. The engine calls it on the receive pump.
/// Implementations must return quickly.
/// </summary>
public interface IChunkSink<TChunk>
{
    void OnChunk(in TChunk chunk);
}
