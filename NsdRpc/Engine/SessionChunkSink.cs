namespace NsdRpc;

// The sink the engine hands to a stream session's pending call. It enqueues
// into the bounded queue and reports overflow through the callback.
internal sealed class SessionChunkSink<TChunk> : IChunkSink<TChunk>
{
    private readonly AsyncChunkQueue<TChunk> _queue;

    public SessionChunkSink(AsyncChunkQueue<TChunk> queue)
    {
        _queue = queue;
    }

    public Action? OnOverflow { get; set; }

    public void OnChunk(in TChunk chunk)
    {
        if (!_queue.TryEnqueue(chunk))
        {
            OnOverflow?.Invoke();
        }
    }
}
