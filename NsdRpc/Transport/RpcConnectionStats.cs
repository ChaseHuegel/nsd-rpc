namespace NsdRpc;

/// <summary>One snapshot of the counters for a connection.</summary>
public readonly struct RpcConnectionStats
{
    public RpcConnectionStats(
        long packetsSent,
        long packetsReceived,
        long bytesSent,
        long bytesReceived,
        int queuedBytes,
        int peakQueuedBytes,
        long writes,
        long rejectedFrames)
    {
        PacketsSent = packetsSent;
        PacketsReceived = packetsReceived;
        BytesSent = bytesSent;
        BytesReceived = bytesReceived;
        QueuedBytes = queuedBytes;
        PeakQueuedBytes = peakQueuedBytes;
        Writes = writes;
        RejectedFrames = rejectedFrames;
    }

    /// <summary>The frames sent.</summary>
    public long PacketsSent { get; }

    /// <summary>The frames received.</summary>
    public long PacketsReceived { get; }

    /// <summary>The bytes sent, including the 4-byte length prefix.</summary>
    public long BytesSent { get; }

    /// <summary>The bytes received, including the 4-byte length prefix.</summary>
    public long BytesReceived { get; }

    /// <summary>The queued send bytes.</summary>
    public int QueuedBytes { get; }

    /// <summary>The highest queued send bytes since construction.</summary>
    public int PeakQueuedBytes { get; }

    /// <summary>The socket writes.</summary>
    public long Writes { get; }

    /// <summary>The refused sends.</summary>
    public long RejectedFrames { get; }
}
