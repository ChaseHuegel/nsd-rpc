namespace NsdRpc;

/// <summary>
/// The handle for one inbound message. A deferred handler keeps it and responds later.
/// </summary>
public readonly struct RpcContext
{
    private readonly RpcPeer _peer;

    internal RpcContext(RpcPeer peer, ulong requestId, ulong method)
    {
        _peer = peer;
        RequestId = requestId;
        Method = method;
    }

    /// <summary>The connection the message arrived on.</summary>
    public IRpcConnection Connection => _peer.Connection;

    /// <summary>The client id the server assigned. Zero on the client side.</summary>
    public ulong ClientId => _peer.ClientId;

    /// <summary>The method id of the inbound message.</summary>
    public ulong Method { get; }

    /// <summary>The request id. Zero for a notification.</summary>
    public ulong RequestId { get; }

    /// <summary>True when the handler runs on the server side.</summary>
    public bool IsServer => _peer.IsServer;

    /// <summary>
    /// The endpoint that received the message. Send to its peers to fan out.
    /// On the server it is the <see cref="RpcServer"/>. On the client it is
    /// the <see cref="RpcClient"/>.
    /// </summary>
    public IRpcHost Host => _peer.Host;

    public void Stream<TChunk>(in TChunk chunk)
        where TChunk : struct
    {
        _peer.StreamAsserting(Method, RequestId, in chunk);
    }

    public void Respond<TResponse>(in RpcResult<TResponse> result)
        where TResponse : struct
    {
        _peer.RespondAsserting(RequestId, in result);
    }

    public void Fail(RpcErrorCode code, string message)
    {
        _peer.FailAsserting(RequestId, code, message);
    }
}
