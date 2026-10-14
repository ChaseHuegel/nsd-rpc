namespace NsdRpc;

// A bounded chunk queue for one stream session. One consumer drains it. The
// session completes it when the response arrives.
internal sealed class AsyncChunkQueue<T>
{
    private readonly object _lock = new();
    private readonly Queue<T> _queue;
    private readonly int _capacity;
    private TaskCompletionSource<bool>? _signal;
    private bool _completed;

    public AsyncChunkQueue(int capacity)
    {
        _capacity = capacity;
        _queue = new Queue<T>(Math.Min(capacity, 64));
    }

    public bool TryEnqueue(T item)
    {
        lock (_lock)
        {
            if (_completed || _queue.Count >= _capacity)
            {
                return false;
            }

            _queue.Enqueue(item);
            _signal?.TrySetResult(true);
            _signal = null;
            return true;
        }
    }

    public void Complete()
    {
        lock (_lock)
        {
            _completed = true;
            _signal?.TrySetResult(true);
            _signal = null;
        }
    }

    public async IAsyncEnumerable<T> EnumerateAll()
    {
        while (true)
        {
            T? item = default;
            bool hasItem = false;
            TaskCompletionSource<bool>? signal = null;
            lock (_lock)
            {
                if (_queue.Count > 0)
                {
                    item = _queue.Dequeue();
                    hasItem = true;
                }
                else if (_completed)
                {
                    yield break;
                }
                else
                {
                    signal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _signal = signal;
                }
            }

            if (hasItem)
            {
                yield return item!;
                continue;
            }

            await signal!.Task.ConfigureAwait(false);
        }
    }
}
