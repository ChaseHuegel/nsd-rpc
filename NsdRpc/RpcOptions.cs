namespace NsdRpc;

/// <summary>
/// Configuration for one RPC peer. The peer reads it at construction.
/// Mutating it after construction is not supported.
/// </summary>
public sealed class RpcOptions
{
    /// <summary>The default call timeout. Zero disables the deadline.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMilliseconds(30000);

    /// <summary>The largest frame a peer accepts or sends, including the 4-byte length prefix.</summary>
    public int MaxFrameBytes { get; set; } = 65570;

    /// <summary>The most pending calls and inbound requests one peer tracks.</summary>
    public int MaxPendingCalls { get; set; } = 1024;

    /// <summary>The most chunks a stream session buffers for one consumer.</summary>
    public int MaxBufferedChunks { get; set; } = 1024;

    /// <summary>A monotonic timestamp source in nanoseconds. Tests inject a fake.</summary>
    public Func<long> TimestampNanos { get; set; } = RpcTime.NowNanos;

    internal static RpcOptions Default { get; } = new();
}
