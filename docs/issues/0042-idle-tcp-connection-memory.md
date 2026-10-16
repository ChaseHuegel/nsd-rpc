# Research: Idle TCP connection memory

- Type: research
- Status: done
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

- [x] The investigation measures the committed bytes of one idle connection at the current defaults. It states the method and the limits of the measurement.
- [x] It compares at least three designs: a lower `ReceiveBufferBytes` default, a buffer rented per read, and a decoder that shrinks after idle periods.
- [x] It records the effect of each design on throughput, syscall count, and steady-state memory.
- [x] It records the effect on [TcpConnection.cs](../../NsdRpc.Tcp/TcpConnection.cs) and [TcpFrameDecoder.cs](../../NsdRpc.Tcp/TcpFrameDecoder.cs).
- [x] It ends with one recommendation and follow-up issues for the accepted design.

## Method

The harness is a scratch console program. It references `NsdRpc.Tcp`. It is
not part of the repository.

Server setup:

- One `TcpServerTransport` and one `RpcServer` with an empty registry.
- The server reads `TcpOptions.ReceiveBufferBytes` from a command-line
  argument.
- The clients are raw `Socket` objects. Each client completes the handshake
  and then sends nothing. A raw client holds no managed receive buffer.

Measurement:

- `GC.GetTotalMemory(forceFullCollection: true)` gives the live managed
  bytes.
- `GC.GetGCMemoryInfo().TotalCommittedBytes` gives the committed managed
  bytes.
- `Process.WorkingSet64` gives the resident bytes.
- Each run measures at two connection counts. It reports the delta divided
  by the added count. The runs use 2,000 added connections.

Limits:

- The client sockets run in the same process as the server. A control run
  with a plain listener measures the socket pair alone. The control costs
  1,072 live bytes per connection. The server cost dominates the control
  cost.
- `GC.GetTotalMemory` forces a full collection. It counts the live heap, not
  the reserved heap.
- The resident bytes include native memory. The kernel socket buffers make
  the number noisy. The live bytes are the reliable number.
- The machine is the baseline machine in [performance](../performance.md):
  AMD Ryzen 5 3600X, Ubuntu 26.04.1, .NET 10.0.12, concurrent workstation
  garbage collector.

## One idle connection at the defaults

The current defaults are `ReceiveBufferBytes` 65,536
([TcpOptions.cs:21](../../NsdRpc.Tcp/TcpOptions.cs:21)) and a decoder that
starts at 4,096
([TcpFrameDecoder.cs:22](../../NsdRpc.Tcp/TcpFrameDecoder.cs:22)).

| Connections | Live bytes per connection | Committed bytes per connection | Resident bytes per connection |
|---|---:|---:|---:|
| Control (socket pair only) | 1,072 | 1,374 | 2,693 |
| Live server, default options | 74,139 | 78,094 | 57,004 |

The receive buffer is 65,536 of the 74,139 live bytes. So 10,000 idle
connections hold about 741 megabytes of live managed memory. The ticket
estimate of about 680 megabytes counts only the two buffers (69,632 bytes
each). The measured number adds the peer and the connection objects.

## The per-connection cost model

The measured live bytes are `ReceiveBufferBytes` plus 8,601. The config
runs confirm the model:

| `ReceiveBufferBytes` | Live bytes per connection |
|---:|---:|
| 4,096 | 12,697 |
| 16,384 | 24,989 |
| 65,536 | 74,139 |

The fixed 8,601 bytes are the decoder at its start size (4,096), the peer,
the connection, the send queue, the pending receive state, and the control
client socket.

## The decoder keeps its high-water mark

`TryGrowBuffer` doubles the buffer up to `MaxFrameBytes` and never shrinks
it ([TcpFrameDecoder.cs:98](../../NsdRpc.Tcp/TcpFrameDecoder.cs:98)). The
default `MaxFrameBytes` is 65,570. One maximum frame grows the buffer to
65,570. The buffer stays at 65,570 after the frame is gone.

One maximum frame per connection grows the live heap by 62,196 bytes per
connection. That matches 65,570 minus 4,096. So a connection that once saw
a maximum frame holds 136,657 live bytes while idle. 10,000 such
connections hold about 1.37 gigabytes.

## Design A: a lower ReceiveBufferBytes default

`ReceiveBufferBytes` is a plain config key. The harness sets it directly.
No code change is needed.

Syscall count (strace, `recvfrom`, 20,000 frames of 4,096 bytes, 81.92 MB
total):

| `ReceiveBufferBytes` | Receive syscalls | Bytes per receive |
|---:|---:|---:|
| 65,536 | 1,272 | 64,403 |
| 16,384 | 5,245 | 15,619 |
| 4,096 | 20,650 | 3,967 |

The kernel returns at most the buffer size in one receive. The receive
syscall count is about the total bytes divided by the buffer size.

Throughput (loopback, no strace, median of three runs):

| `ReceiveBufferBytes` | 4 KB frames | 60 KB frames |
|---:|---:|---:|
| 4,096 | 1,250 MB/s | 1,050 MB/s |
| 16,384 | 2,400 MB/s | 1,820 MB/s |
| 32,768 | — | 2,600 MB/s |
| 65,536 | 2,650 MB/s | 2,400 MB/s |

Effect: A lowers the idle footprint in proportion to the default. A default
of 16,384 gives about 25 KB per connection. But a small default costs
throughput on a busy link. A 60 KB frame stream drops from about 2,400 to
1,820 MB/s at 16,384. A default of 4,096 is about two times slower.

## Design B: rent the receive buffer per read

The receive loop rents `ReceiveBufferBytes` before each read and returns
the buffer after `TryPush`
([TcpConnection.cs:283](../../NsdRpc.Tcp/TcpConnection.cs:283)). An idle
connection holds no receive buffer.

- Steady-state memory: the decoder plus the fixed objects, about 8,601 live
  bytes per idle connection. That is the smallest of the three designs.
- Syscall count: the same as design A at the same size. B keeps the current
  65,536 size, so the count matches the current count (1,272 in the 81.92
  MB run above).
- Throughput: the same as the current design, within the rent cost. Rent
  plus clear plus return of a 65,536-byte array costs about 595 ns
  (measured). A 65,536-byte read stream does about 40,000 reads per second,
  so the cost is about 2.4 percent. Clear the received length only to cut
  the clear cost.
- Correctness: the buffer is live from the `Rent` call to the `Return`
  call. The `await ReceiveAsync` call and the `TryPush` call both sit
  between. `TryPush` copies the read into the decoder buffer before it
  calls the frame handler
  ([TcpFrameDecoder.cs:52](../../NsdRpc.Tcp/TcpFrameDecoder.cs:52)). So the
  receive buffer can return before the handler runs.

## Design C: shrink the decoder after idle

`TryGrowBuffer` never shrinks. C adds a shrink to the start size after an
idle period.

- Steady-state memory: C bounds the decoder at the start size (4,096). It
  removes the 61,474-byte high-water from an idle connection.
- Throughput: no effect. C touches the decoder between reads only.
- Syscall count: no effect.
- Trigger: a shrink needs an idle trigger. The receive loop waits in
  `ReceiveAsync`, so it cannot shrink on its own. A shrink on the next read
  is too late, because a truly idle connection never reads again. C needs a
  timer or a periodic pass. A timer per connection costs memory and works
  against the goal. One timer for the transport is cheaper.
- Thrash: a shrink after every complete frame would free and reallocate the
  buffer for a stream of large frames. The shrink needs an idle gap.

## Effect on the two files

`TcpConnection.cs`:

- Design A: no code change. The default moves in `TcpOptions.cs`.
- Design B: the receive loop rents and returns the buffer per read. The
  `finally` return moves into the loop. The `ReceiveBufferBytes` name stops
  matching its meaning. Rename it or document it as the per-read size.
- Design C: the connection owns the idle trigger, or the transport passes
  one in.

`TcpFrameDecoder.cs`:

- Design C: the decoder gains a shrink method and a start-size field.
  `TryGrowBuffer` keeps the high-water. The shrink resets it.
- Design A and design B: no change.

## Recommendation

Rent the receive buffer per read (design B) and keep `ReceiveBufferBytes`
at 65,536. B removes the dominant idle cost. It keeps the syscall count and
the throughput. One idle connection drops from 74,139 live bytes to about
8,601.

Then shrink the decoder after idle (design C) to bound the high-water mark.
C is worth it only when large frames are common. Without C, a connection
that saw a maximum frame stays at 136,657 live bytes.

Do not lower `ReceiveBufferBytes` as the primary fix. A small default cuts
the throughput on a busy link by about two times.

Follow-up issues:

- [0052](../issues/0052-tcp-receive-buffer-per-read.md) — design B.
- [0053](../issues/0053-tcp-decoder-idle-shrink.md) — design C.

