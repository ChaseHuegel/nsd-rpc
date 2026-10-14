namespace NsdRpc;

/// <summary>
/// One RPC connection. The transport owns the byte stream. The peer owns the protocol.
/// </summary>
public interface IRpcConnection
{
    /// <summary>True while the connection can send and receive.</summary>
    bool IsConnected { get; }

    /// <summary>
    /// Queues one complete frame for sending. Copies the bytes before it returns.
    /// A frame is a 4-byte little-endian length prefix and the envelope bytes.
    /// Returns false when the frame was rejected: a closed connection or a full queue.
    /// </summary>
    bool TrySend(ReadOnlySpan<byte> frame);

    /// <summary>Fires once when the connection dies. The peer faults pending calls.</summary>
    event Action Disconnected;
}
