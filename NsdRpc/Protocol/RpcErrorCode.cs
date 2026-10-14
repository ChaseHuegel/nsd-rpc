namespace NsdRpc;

/// <summary>
/// A stable RPC error code from the JSON-RPC numeric space.
/// </summary>
public enum RpcErrorCode
{
    /// <summary>The envelope is malformed for its kind.</summary>
    InvalidRequest = -32600,

    /// <summary>No handler is registered, or the direction is wrong.</summary>
    MethodNotFound = -32601,

    /// <summary>The payload did not decode.</summary>
    InvalidParams = -32602,

    /// <summary>The handler threw. The wire carries no internal detail.</summary>
    InternalError = -32603,

    /// <summary>The call passed its deadline. Local only.</summary>
    RequestTimeout = -32001,

    /// <summary>Reserved. No authorization hook exists yet.</summary>
    PermissionDenied = -32002,

    /// <summary>The connection is down or gone. Local only.</summary>
    Unavailable = -32003,

    /// <summary>The receiver is at capacity. Also used for a full stream queue.</summary>
    Busy = -32004,

    /// <summary>The caller's token fired. Local only, never on the wire.</summary>
    Cancelled = -32005,
}
