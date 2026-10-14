namespace NsdRpc;

/// <summary>
/// Which side may handle a method. The dispatcher rejects the other side.
/// </summary>
public enum RpcDirection
{
    /// <summary>Both peers may handle the method.</summary>
    Both = 0,

    /// <summary>Only the server side may handle the method.</summary>
    ClientToServer = 1,

    /// <summary>Only the client side may handle the method.</summary>
    ServerToClient = 2,
}
