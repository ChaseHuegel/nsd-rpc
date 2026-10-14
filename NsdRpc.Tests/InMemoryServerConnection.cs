using System.Net;
using NsdRpc;

namespace NsdRpc.Tests;

// Wraps an in-memory connection as the server end. The peer reads ClientId
// and IsServer from it.
internal sealed class InMemoryServerConnection : IServerConnection
{
    private readonly InMemoryConnection _inner;

    public InMemoryServerConnection(InMemoryConnection inner, ulong clientId)
    {
        _inner = inner;
        ClientId = clientId;
    }

    public ulong ClientId { get; }

    public EndPoint? RemoteEndPoint => null;

    public bool IsConnected => _inner.IsConnected;

    public event Action Disconnected
    {
        add => _inner.Disconnected += value;
        remove => _inner.Disconnected -= value;
    }

    public bool TrySend(ReadOnlySpan<byte> frame)
    {
        return _inner.TrySend(frame);
    }
}
