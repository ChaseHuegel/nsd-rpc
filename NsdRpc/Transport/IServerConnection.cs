using System.Net;

namespace NsdRpc;

/// <summary>
/// A server-side connection. The server transport assigns the client id at accept.
/// </summary>
public interface IServerConnection : IRpcConnection
{
    /// <summary>The id the server assigned at accept. Never zero.</summary>
    ulong ClientId { get; }

    /// <summary>The remote endpoint, when the transport has one.</summary>
    EndPoint? RemoteEndPoint { get; }
}
