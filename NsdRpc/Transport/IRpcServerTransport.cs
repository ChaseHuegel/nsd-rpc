namespace NsdRpc;

/// <summary>
/// A listener that accepts server-side connections.
/// </summary>
public interface IRpcServerTransport
{
    /// <summary>Accepts one connection. Returns null when closed.</summary>
    Task<IServerConnection?> AcceptAsync();

    /// <summary>Stops accepting and closes every accepted connection.</summary>
    void Close();
}
