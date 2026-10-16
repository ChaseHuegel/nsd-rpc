# Bug: The TCP accept loop stalls on a silent client

- Type: bug
- Status: open
- Workflow: ../specs/issues.md

## Problem

`TcpServerTransport.AcceptAsync` accepts one socket, then awaits the
handshake before it accepts the next socket:
[TcpServerTransport.cs:71](../../NsdRpc.Tcp/TcpServerTransport.cs:71).

`ReadExactAsync` has no deadline
([TcpConnection.cs:211](../../NsdRpc.Tcp/TcpConnection.cs:211)). `TcpOptions`
has no handshake timeout. A client that connects and sends no handshake
bytes stalls every new accept while it stays connected. One silent client
stops the server from accepting new clients.

## Acceptance criteria

- [ ] A silent client does not block other clients. A test opens a socket, sends nothing, connects a second client, and completes a call.
- [ ] `TcpOptions` has a handshake deadline key. No magic constant exists in the code.
- [ ] A test with a short configured deadline proves that the transport drops a silent client.
- [ ] Accept order still assigns unique ascending client ids.
- [ ] The docs pass documents the new key in a TCP options table per [development.md](../development.md).
