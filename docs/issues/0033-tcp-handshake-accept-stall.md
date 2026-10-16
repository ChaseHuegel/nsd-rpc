# Bug: The TCP accept loop stalls on a silent client

- Type: bug
- Status: done
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

- [x] A silent client does not block other clients. A test opens a socket, sends nothing, connects a second client, and completes a call.
- [x] `TcpOptions` has a handshake deadline key. No magic constant exists in the code.
- [x] A test with a short configured deadline proves that the transport drops a silent client.
- [x] Accept order still assigns unique ascending client ids.
- [x] The docs pass documents the new key in a TCP options table per [development.md](../development.md).

## Resolution

The server transport accepts in a background loop and completes each
handshake in its own task. Completed connections enter a ready queue that
`AcceptAsync` drains. A failed or expired handshake closes the connection and
is skipped.

`TcpOptions.HandshakeTimeout` bounds one handshake and defaults to 10
seconds. Zero or negative disables it. The table lives in
[rpc-api](../specs/rpc-api.md).
