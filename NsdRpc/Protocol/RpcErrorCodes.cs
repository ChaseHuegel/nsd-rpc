namespace NsdRpc;

/// <summary>
/// Constants for the RPC error code space. See docs/specs/rpc-protocol.md.
/// </summary>
public static class RpcErrorCodes
{
    /// <summary>The smallest valid app error code. App codes go down from here.</summary>
    public const int AppCodeMin = -32100;
}
