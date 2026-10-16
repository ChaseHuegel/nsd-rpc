# Bug: TcpServerTransport leaks accepted connections

- Type: bug
- Status: open
- Workflow: ../specs/issues.md

## Problem

`TcpServerTransport.AcceptAsync` adds each accepted connection to the
`_accepted` list:
[TcpServerTransport.cs:68](../../NsdRpc.Tcp/TcpServerTransport.cs:68).

The transport removes the connection only in two cases:

- A failed handshake ([TcpServerTransport.cs:80](../../NsdRpc.Tcp/TcpServerTransport.cs:80)).
- `Close` ([TcpServerTransport.cs:91](../../NsdRpc.Tcp/TcpServerTransport.cs:91)).

A client that disconnects after a successful handshake stays in the list
until the transport closes. The entry keeps the `TcpConnection` and its
decoder buffer alive. The decoder buffer holds up to `MaxFrameBytes + 4`
bytes. A server with many connect and disconnect cycles grows without a
bound.

## Acceptance criteria

- [ ] A disconnect removes the connection from `_accepted`. `RpcServer` already uses this pattern for peers ([RpcServer.cs:127](../../NsdRpc/RpcServer.cs:127)).
- [ ] A test connects a client, disconnects it, and proves the list shrinks. Use the internal access from `InternalsVisibleTo`.
- [ ] A test runs many connect and disconnect cycles and proves the list stays bounded.
- [ ] `Close` stays safe when a disconnect races the close.
- [ ] The memory of one connection is released after the disconnect.
