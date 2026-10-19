# Improvement: Send batching

- Type: improvement
- Status: done
- Workflow: ../specs/issues.md

## Problem

`DrainSendQueueAsync` writes one frame per socket write
([TcpConnection.cs:165](../../NsdRpc.Tcp/TcpConnection.cs:165)). A world stream
sends hundreds of small frames. A per-tick publisher sends one frame per
peer per interval. Each frame costs one syscall and one small segment.

A consumer coalesces frames today to stay efficient. The transport should
offer that policy.

## Acceptance criteria

- [x] `TcpOptions.SendBatchInterval` defaults to zero. Zero sends
      immediately.
- [x] `TcpOptions.SendBatchMaxBytes` defaults to 65,536.
- [x] The send loop drains queued frames into one pooled buffer, up to the
      byte limit, and writes once.
- [x] A frame is never split. A frame larger than the limit is written alone.
- [x] The interval coalesces frames that arrive during the wait. A send
      signal wakes the loop early.
- [x] `Packets` counts frames and `Writes` counts socket writes. Issue 0058
      defined both. A batched exchange pins both.
- [x] A test proves N frames produce one write at a nonzero interval.
- [x] A test proves the byte limit is respected.
- [x] A test proves a large frame is sent alone.
- [x] A benchmark records syscall count and throughput before and after.
- [x] The docs pass updates the TCP options tables and `performance`.
- [x] No wire change.

## Implementation notes

- Default zero keeps the current behavior. A consumer opts into an interval.
- Do not hold the send lock during the socket write. Take frames, release
  the lock, then write.
- Return every pooled buffer on every path, including a write failure.
- A single frame larger than `SendBatchMaxBytes` must not deadlock the
  drain. Write it alone.
- The batch wait must not outlast the remaining backlog window. Wait for the
  smaller of `SendBatchInterval` and the time left in
  `SendBacklogDisconnectMs`, or until the send signal fires. This preserves
  the backlog disconnect from issue 0056.

## Resolution

`TcpOptions.SendBatchInterval` defaults to zero, which keeps one write per
frame. A positive interval waits for more frames, then drains up to
`SendBatchMaxBytes` into one pooled buffer and writes once. A frame larger
than the limit is written alone. A full batch signals the wait early, and the
connection lifetime cancels it. `Packets` counts frames and `Writes` counts
socket writes.

Benchmark, 4,096 eight-byte frames over loopback: immediate 27.98 ms and
4,096 writes, batched at 1 ms 1.219 ms and one write. The batched path is 23
times faster. See [performance](../../performance.md).
