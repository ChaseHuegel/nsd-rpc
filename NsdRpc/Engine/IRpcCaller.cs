namespace NsdRpc;

/// <summary>
/// The call surface shared by RpcClient and RpcPeer. The generated service
/// clients forward through it, so one client works over either.
/// </summary>
public interface IRpcCaller
{
    Task<RpcResult<TResponse>> InvokeAsync<TRequest, TResponse>(
        ulong method,
        in TRequest request,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where TRequest : struct
        where TResponse : struct;

    Task<RpcResult<TResponse>> InvokeStreamAsync<TRequest, TChunk, TResponse>(
        ulong method,
        in TRequest request,
        IChunkSink<TChunk> sink,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where TRequest : struct
        where TChunk : struct
        where TResponse : struct;

    RpcStreamSession<TChunk, TResponse> InvokeStreamAsync<TRequest, TChunk, TResponse>(
        ulong method,
        in TRequest request,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where TRequest : struct
        where TChunk : struct
        where TResponse : struct;

    bool Notify<TRequest>(ulong method, in TRequest request)
        where TRequest : struct;

    bool Notify(ulong method);
}
