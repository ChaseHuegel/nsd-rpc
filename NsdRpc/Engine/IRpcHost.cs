namespace NsdRpc;

/// <summary>
/// An RPC endpoint that reaches one or more peers. A caller selects a peer
/// from <see cref="Peers"/> and calls it. A single peer is a one-element set.
/// </summary>
public interface IRpcHost
{
    /// <summary>The peers this endpoint reaches. An immutable snapshot.</summary>
    PeerSet Peers { get; }
}
