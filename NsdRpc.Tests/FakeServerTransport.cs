using NsdRpc;

namespace NsdRpc.Tests;

// A listener the tests drive. AcceptAsync parks until a connection is
// enqueued or Close runs. Close drops queued connections and releases a
// parked accept with null.
internal sealed class FakeServerTransport : IRpcServerTransport
{
    private readonly object _lock = new();
    private readonly Queue<IServerConnection> _pending = new();
    private TaskCompletionSource<IServerConnection?>? _acceptWaiter;
    private bool _closed;

    public int AcceptedCount { get; private set; }

    public void Enqueue(IServerConnection connection)
    {
        TaskCompletionSource<IServerConnection?>? waiter;
        lock (_lock)
        {
            if (_closed)
            {
                return;
            }

            waiter = _acceptWaiter;
            if (waiter == null)
            {
                _pending.Enqueue(connection);
                return;
            }

            _acceptWaiter = null;
            AcceptedCount++;
        }

        waiter.TrySetResult(connection);
    }

    public Task<IServerConnection?> AcceptAsync()
    {
        lock (_lock)
        {
            if (_closed)
            {
                return Task.FromResult<IServerConnection?>(null);
            }

            if (_pending.Count > 0)
            {
                AcceptedCount++;
                return Task.FromResult<IServerConnection?>(_pending.Dequeue());
            }

            if (_acceptWaiter != null)
            {
                throw new InvalidOperationException("Only one accept may run at a time.");
            }

            _acceptWaiter = new TaskCompletionSource<IServerConnection?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            return _acceptWaiter.Task;
        }
    }

    public void Close()
    {
        TaskCompletionSource<IServerConnection?>? waiter;
        lock (_lock)
        {
            _closed = true;
            _pending.Clear();
            waiter = _acceptWaiter;
            _acceptWaiter = null;
        }

        waiter?.TrySetResult(null);
    }
}
