namespace NsdRpc;

/// <summary>
/// One stream call. Consume the chunks, then await the response. A full
/// chunk queue faults the call with Busy.
/// </summary>
public sealed class RpcStreamSession<TChunk, TResponse>
{
    private readonly AsyncChunkQueue<TChunk> _queue;

    internal RpcStreamSession(Task<RpcResult<TResponse>> response, AsyncChunkQueue<TChunk> queue)
    {
        Response = response;
        _queue = queue;
        _ = CompleteWhenResponseArrivesAsync();
    }

    /// <summary>The final response. It completes after the last chunk.</summary>
    public Task<RpcResult<TResponse>> Response { get; }

    /// <summary>The chunks in arrival order. The sequence ends with the response.</summary>
    public IAsyncEnumerable<TChunk> Chunks => _queue.EnumerateAll();

    private async Task CompleteWhenResponseArrivesAsync()
    {
        await Response.ConfigureAwait(false);
        _queue.Complete();
    }
}
