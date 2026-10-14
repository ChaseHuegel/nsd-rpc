namespace NsdRpc;

/// <summary>
/// The kind of one RPC envelope. The wire stores the raw byte value.
/// </summary>
public enum RpcMessageKind : byte
{
    /// <summary>Invalid. A zero-initialized envelope fails validation.</summary>
    Unknown = 0,

    /// <summary>A call. A response is required.</summary>
    Request = 1,

    /// <summary>The answer to one request.</summary>
    Response = 2,

    /// <summary>A one-way message, or one chunk of a stream.</summary>
    Notification = 3,

    /// <summary>Stop the request named by the envelope id. No response, no ack.</summary>
    Cancellation = 4,
}
