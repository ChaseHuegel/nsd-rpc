# Bug: TcpServerTransport leaks accepted connections

- Type: bug
- Status: done
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

- [x] A disconnect removes the connection from `_accepted`. `RpcServer` already uses this pattern for peers ([RpcServer.cs:127](../../NsdRpc/RpcServer.cs:127)).
- [x] A test connects a client, disconnects it, and proves the list shrinks. Use the internal access from `InternalsVisibleTo`.
- [x] A test runs many connect and disconnect cycles and proves the list stays bounded.
- [x] `Close` stays safe when a disconnect races the close.
- [x] The memory of one connection is released after the disconnect.

## Resolution

The transport subscribes to each accepted connection's disconnect and removes
the entry. The list held the only strong reference to the connection, so the
removal releases it. The list stays bounded by the live connection count.

`Close` snapshots the list before it closes the connections, so a disconnect
handler cannot mutate the list during the loop.
