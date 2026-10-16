namespace NsdRpc;

/// <summary>
/// An RPC endpoint that reaches one or more peers. The call surface delivers
/// to every peer the endpoint reaches.
/// </summary>
public interface IRpcHost : IRpcCaller
{
    /// <summary>The peers this endpoint reaches. A snapshot.</summary>
    IReadOnlyCollection<RpcPeer> Peers { get; }
}
