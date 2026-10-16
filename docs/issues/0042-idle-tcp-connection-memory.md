# Research: Idle TCP connection memory

- Type: research
- Status: open
- Workflow: ../specs/issues.md

## Problem

One live connection holds memory that scales with the connection count.

- The receive loop rents `TcpOptions.ReceiveBufferBytes` bytes from the
  shared pool for the connection's life:
  [TcpConnection.cs:151](../../NsdRpc.Tcp/TcpConnection.cs:151). The
  default is 65,536:
  [TcpOptions.cs:18](../../NsdRpc.Tcp/TcpOptions.cs:18).
- The decoder starts at 4,096 bytes and keeps its high-water mark, up to
  `MaxFrameBytes + 4`:
  [TcpFrameDecoder.cs:18](../../NsdRpc.Tcp/TcpFrameDecoder.cs:18),
  [TcpFrameDecoder.cs:44](../../NsdRpc.Tcp/TcpFrameDecoder.cs:44).

A server with 10,000 idle connections holds about 680 megabytes. No
measurement records the real footprint.

[0031](../issues/0031-tcp-accepted-connection-leak.md) fixes a leak in the
same area. This question is about the steady-state cost of a live
connection.

Question: which design gives the smallest idle footprint without a
meaningful throughput loss?

## Acceptance criteria

- [ ] The investigation measures the committed bytes of one idle connection at the current defaults. It states the method and the limits of the measurement.
- [ ] It compares at least three designs: a lower `ReceiveBufferBytes` default, a buffer rented per read, and a decoder that shrinks after idle periods.
- [ ] It records the effect of each design on throughput, syscall count, and steady-state memory.
- [ ] It records the effect on [TcpConnection.cs](../../NsdRpc.Tcp/TcpConnection.cs) and [TcpFrameDecoder.cs](../../NsdRpc.Tcp/TcpFrameDecoder.cs).
- [ ] It ends with one recommendation and follow-up issues for the accepted design.
