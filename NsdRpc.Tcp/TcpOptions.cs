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

    /// <summary>Disables Nagle's algorithm when true.</summary>
    public bool NoDelay { get; set; } = true;

    /// <summary>The deadline for one handshake. Zero or negative disables it.</summary>
    public TimeSpan HandshakeTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>The receive buffer size per read. The receive loop rents it per read.</summary>
    public int ReceiveBufferBytes { get; set; } = 65536;

    /// <summary>The largest queued send bytes per connection. A full queue rejects sends.</summary>
    public int SendQueueBytes { get; set; } = 1048576;
}
