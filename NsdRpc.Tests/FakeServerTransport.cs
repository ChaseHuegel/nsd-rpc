using NsdRpc;

namespace NsdRpc.Tests;

// A listener with a fixed queue of pending connections. AcceptAsync dequeues
// in order. Close stops the queue and nulls the next accept.
internal sealed class FakeServerTransport : IRpcServerTransport
{
    private readonly Queue<IServerConnection> _pending = new();
    private bool _closed;

    public int AcceptCount { get; private set; }

    public void Enqueue(IServerConnection connection)
    {
        _pending.Enqueue(connection);
    }

    public Task<IServerConnection?> AcceptAsync()
    {
        if (_closed)
        {
            return Task.FromResult<IServerConnection?>(null);
        }

        AcceptCount++;
        IServerConnection? next = _pending.Count > 0 ? _pending.Dequeue() : null;
        return Task.FromResult(next);
    }

    public void Close()
    {
        _closed = true;
    }
}
