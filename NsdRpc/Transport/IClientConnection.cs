namespace NsdRpc;

/// <summary>
/// A client-side connection. The client owns one. Connect before use.
/// </summary>
public interface IClientConnection : IRpcConnection
{
    /// <summary>Connects. Fails when the connection cannot be established.</summary>
    Task ConnectAsync();
}
