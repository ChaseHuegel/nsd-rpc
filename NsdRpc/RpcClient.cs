namespace NsdRpc;

/// <summary>
/// One client endpoint: a peer over one connection plus the handler dispatcher.
/// Own the instance for the connection's lifetime. It owns the id space.
/// </summary>
public sealed class RpcClient : IRpcCaller
{
    private readonly RpcPeer _peer;

    public RpcClient(IRpcConnection connection, RpcRegistry registry, RpcOptions? options = null)
    {
        if (connection == null)
        {
            throw new ArgumentNullException(nameof(connection));
        }

        if (registry == null)
        {
            throw new ArgumentNullException(nameof(registry));
        }

        _peer = new RpcPeer(connection, options);
        _peer.DispatchHook = new RpcDispatcher(registry, _peer).DispatchAsync;
    }

    public Task<RpcResult<TResponse>> InvokeAsync<TRequest, TResponse>(
        ulong method,
        in TRequest request,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where TRequest : struct
        where TResponse : struct
    {
        return _peer.InvokeAsync<TRequest, TResponse>(method, in request, timeout, cancellationToken);
    }

    public Task<RpcResult<TResponse>> InvokeStreamAsync<TRequest, TChunk, TResponse>(
        ulong method,
        in TRequest request,
        IChunkSink<TChunk> sink,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where TRequest : struct
        where TChunk : struct
        where TResponse : struct
    {
        return _peer.InvokeStreamAsync<TRequest, TChunk, TResponse>(
            method, in request, sink, timeout, cancellationToken);
    }

    public RpcStreamSession<TChunk, TResponse> InvokeStreamAsync<TRequest, TChunk, TResponse>(
        ulong method,
        in TRequest request,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where TRequest : struct
        where TChunk : struct
        where TResponse : struct
    {
        return _peer.InvokeStreamAsync<TRequest, TChunk, TResponse>(
            method, in request, timeout, cancellationToken);
    }

    public bool Notify<TRequest>(ulong method, in TRequest request)
        where TRequest : struct
    {
        return _peer.Notify(method, in request);
    }

    public bool Notify(ulong method)
    {
        return _peer.Notify(method);
    }

    public void HandleFrame(ReadOnlySpan<byte> frame)
    {
        _peer.HandleFrame(frame);
    }

    public void CheckTimeouts(long timestampNanos)
    {
        _peer.CheckTimeouts(timestampNanos);
    }

    public void Poll()
    {
        _peer.Poll();
    }
}
