using System.Net;

namespace NsdRpc.Tcp;

/// <summary>
/// Configuration for the TCP transport. Read at construction.
/// </summary>
public sealed class TcpOptions
{
    /// <summary>The remote host for the client. The listener ignores it.</summary>
    public string Host { get; set; } = "localhost";

    /// <summary>The port. Zero binds an ephemeral port on the server.</summary>
    public int Port { get; set; } = 0;

    /// <summary>The local address the listener binds. Defaults to IPAddress.Any.</summary>
    public IPAddress BindAddress { get; set; } = IPAddress.Any;

    /// <summary>The deadline for one connect attempt. Zero or negative disables it.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Disables Nagle's algorithm when true.</summary>
    public bool NoDelay { get; set; } = true;

    /// <summary>The deadline for one handshake. Zero or negative disables it.</summary>
    public TimeSpan HandshakeTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Closes the connection when no bytes arrive for this period. Zero or negative disables it.</summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.Zero;

    /// <summary>The receive buffer size per read. The receive loop rents it per read.</summary>
    public int ReceiveBufferBytes { get; set; } = 65536;

    /// <summary>The largest buffered receive bytes before a subscriber attaches.</summary>
    public int ReceiveQueueBytes { get; set; } = 1048576;

    /// <summary>The decoder shrinks to its start size after this idle period. Zero or negative disables it.</summary>
    public TimeSpan DecoderIdleShrinkAfter { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>The largest queued send bytes per connection. A full queue rejects sends.</summary>
    public int SendQueueBytes { get; set; } = 1048576;

    /// <summary>Coalesces queued frames for this interval into one write. Zero sends immediately.</summary>
    public TimeSpan SendBatchInterval { get; set; } = TimeSpan.Zero;

    /// <summary>The largest bytes in one batched write. A larger frame is written alone.</summary>
    public int SendBatchMaxBytes { get; set; } = 65536;

    /// <summary>The queued send bytes that start the backlog window. Zero disables the check.</summary>
    public int SendBacklogDisconnectBytes { get; set; } = 0;

    /// <summary>The window in milliseconds for the backlog disconnect. Zero disables the check.</summary>
    public int SendBacklogDisconnectMs { get; set; } = 0;
}
