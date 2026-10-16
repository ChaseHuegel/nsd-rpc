using System.Threading.Tasks.Sources;

namespace NsdRpc;

// A bounded chunk queue for one stream session. One consumer drains it. The
// session completes it when the response arrives.
//
// The queue owns one reusable wait source, so a park allocates no signal.
internal sealed class AsyncChunkQueue<T> : IValueTaskSource
{
    private readonly object _lock = new();
    private readonly Queue<T> _queue;
    private readonly int _capacity;
    private ManualResetValueTaskSourceCore<bool> _signal;
    private bool _waiting;
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
            SignalLocked();
            return true;
        }
    }

    public void Complete()
    {
        lock (_lock)
        {
            _completed = true;
            SignalLocked();
        }
    }

    public async IAsyncEnumerable<T> EnumerateAll()
    {
        while (true)
        {
            T? item = default;
            bool hasItem = false;
            bool finished = false;
            ValueTask wait = default;
            lock (_lock)
            {
                if (_queue.Count > 0)
                {
                    item = _queue.Dequeue();
                    hasItem = true;
                }
                else if (_completed)
                {
                    finished = true;
                }
                else
                {
                    _waiting = true;
                    wait = new ValueTask(this, _signal.Version);
                }
            }

            if (hasItem)
            {
                yield return item!;
                continue;
            }

            if (finished)
            {
                yield break;
            }

            await wait.ConfigureAwait(false);
            _signal.Reset();
        }
    }

    private void SignalLocked()
    {
        if (_waiting)
        {
            _waiting = false;
            _signal.SetResult(true);
        }
    }

    ValueTaskSourceStatus IValueTaskSource.GetStatus(short token)
    {
        return _signal.GetStatus(token);
    }

    void IValueTaskSource.OnCompleted(
        Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
    {
        _signal.OnCompleted(continuation, state, token, flags);
    }

    void IValueTaskSource.GetResult(short token)
    {
        _signal.GetResult(token);
    }
}
