namespace NsdRpc;

/// <summary>Why a connection closed.</summary>
public enum RpcDisconnectReason
{
    /// <summary>The peer closed its send side or a read returned zero bytes.</summary>
    PeerClosed,

    /// <summary>A socket read failed.</summary>
    ReadError,

    /// <summary>A socket write failed.</summary>
    WriteError,

    /// <summary>No bytes arrived within <c>TcpOptions.IdleTimeout</c>.</summary>
    IdleTimeout,

    /// <summary>The handshake failed or passed its deadline.</summary>
    HandshakeFailed,

    /// <summary>The outbound backlog stayed above its threshold for too long.</summary>
    BacklogLimit,

    /// <summary>The local host closed the connection.</summary>
    LocalClose,

    /// <summary>The peer broke the framing or protocol rules.</summary>
    ProtocolViolation,
}
