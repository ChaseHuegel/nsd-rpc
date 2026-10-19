namespace NsdRpc;

/// <summary>
/// One RPC connection. The transport owns the byte stream. The peer owns the protocol.
/// </summary>
public interface IRpcConnection
{
    /// <summary>True while the connection can send and receive.</summary>
    bool IsConnected { get; }

    /// <summary>Closes the connection. Fires Disconnected once with the reason.</summary>
    void Close(RpcDisconnectReason reason = RpcDisconnectReason.LocalClose);

    /// <summary>
    /// Queues one complete frame for sending. Copies the bytes before it returns.
    /// A frame is a 4-byte little-endian length prefix and the envelope bytes.
    /// Returns false when the frame was rejected: a closed connection or a full queue.
    /// </summary>
    bool TrySend(ReadOnlySpan<byte> frame);

    /// <summary>Fires once when the connection dies, with the reason.</summary>
    event Action<RpcDisconnectReason> Disconnected;

    /// <summary>
    /// Fires from the transport's receive loop for each complete frame. The
    /// span is only valid during the call. Pull-based transports never fire it.
    /// </summary>
    event RpcFrameHandler FrameReceived;
}
