# Improvement: Rent the TCP receive buffer per read

- Type: improvement
- Status: done
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

- [x] The receive loop rents the buffer before each read and returns it
      after `TryPush` returns.
- [x] An idle connection holds no receive buffer.
- [x] A measurement records the idle footprint and shows the receive buffer
      is gone. It uses the method in [0042](0042-idle-tcp-connection-memory.md).
- [x] The receive syscall count and the throughput match the current design
      at the default size.
- [x] The loop returns the buffer on every path: a normal read, a decoder
      rejection, and a socket failure.
- [x] The option name and its docs match the per-read meaning.
- [x] Tests pin the per-read rent and the returned buffer on each terminal
      path.

## Resolution

`ReceiveOnceAsync` rents the buffer, reads once, pushes, and returns the
buffer
([TcpConnection.cs:283](../../NsdRpc.Tcp/TcpConnection.cs:283)). The buffer is
a local of one receive step, so no connection field holds it across reads.

The loop waits for data before it rents
([TcpConnection.cs:259](../../NsdRpc.Tcp/TcpConnection.cs:259)). The wait is a
zero-length receive. A zero-length receive returns when the socket is
readable and does not consume the data. The wait holds no buffer, so an idle
connection holds none. The wait adds one zero-length receive per read. The
zero-length receive carries no data, so the data receive count is unchanged.
The loopback throughput matches within the run-to-run spread.

`ReceiveBufferBytes` keeps its name and its docs now state the per-read use
([TcpOptions.cs:21](../../NsdRpc.Tcp/TcpOptions.cs:21)).

## Measurement

The harness is the scratch console program from
[0042](0042-idle-tcp-connection-memory.md). It is not part of the repository.
It starts one `TcpServerTransport` and one `RpcServer` with an empty registry.
Raw `Socket` clients complete the handshake and then send nothing. It measures
`GC.GetTotalMemory(forceFullCollection: true)` at two connection counts and
divides the delta by the added count.

| Design | Live bytes per idle connection |
|---|---:|
| One buffer for the connection's life (0042, the current design) | 74,139 |
| A rent per read (this design) | 8,920 |

The receive buffer is 65,536 bytes at the default. The after number drops by
about 65,219 bytes, so the receive buffer is gone from an idle connection. The
remainder is the decoder at its start size, the peer, the connection, and the
send queue. The method, the limits, and the machine match
[0042](0042-idle-tcp-connection-memory.md).
