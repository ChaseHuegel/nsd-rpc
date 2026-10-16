# Improvement: Rent the TCP receive buffer per read

- Type: improvement
- Status: open
- Workflow: ../specs/issues.md

## Problem

The receive loop rents one buffer for the life of the connection. The buffer
is `TcpOptions.ReceiveBufferBytes` bytes
([TcpConnection.cs:212](../../NsdRpc.Tcp/TcpConnection.cs:212)). The default
is 65,536 ([TcpOptions.cs:21](../../NsdRpc.Tcp/TcpOptions.cs:21)). The loop
holds the buffer while the connection is idle.

Issue [0042](0042-idle-tcp-connection-memory.md) measures the cost and
accepts design B. One idle connection holds 74,139 live bytes at the
defaults. The receive buffer is 65,536 of them. A rent per read removes the
buffer from an idle connection with no extra syscalls and about 2.4 percent
extra CPU on a full 65,536-byte read stream.

## Acceptance criteria

- [ ] The receive loop rents the buffer before each read and returns it
      after `TryPush` returns.
- [ ] An idle connection holds no receive buffer.
- [ ] A measurement records the idle footprint and shows the receive buffer
      is gone. It uses the method in [0042](0042-idle-tcp-connection-memory.md).
- [ ] The receive syscall count and the throughput match the current design
      at the default size.
- [ ] The loop returns the buffer on every path: a normal read, a decoder
      rejection, and a socket failure.
- [ ] The option name and its docs match the per-read meaning.
- [ ] Tests pin the per-read rent and the returned buffer on each terminal
      path.
