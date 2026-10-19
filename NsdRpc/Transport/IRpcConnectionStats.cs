namespace NsdRpc;

/// <summary>An optional transport capability that reports traffic counters.</summary>
public interface IRpcConnectionStats
{
    /// <summary>A snapshot of the connection counters.</summary>
    RpcConnectionStats Stats { get; }
}
