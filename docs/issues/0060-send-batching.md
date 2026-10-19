# Improvement: Send batching

- Type: improvement
- Status: open
- Workflow: ../specs/issues.md

## Problem

`DrainSendQueueAsync` writes one frame per socket write
([TcpConnection.cs:165](../../NsdRpc.Tcp/TcpConnection.cs:165)). A world stream
sends hundreds of small frames. A per-tick publisher sends one frame per
peer per interval. Each frame costs one syscall and one small segment.

A consumer coalesces frames today to stay efficient. The transport should
offer that policy.

## Acceptance criteria

- [ ] `TcpOptions.SendBatchInterval` defaults to zero. Zero sends
      immediately.
- [ ] `TcpOptions.SendBatchMaxBytes` defaults to 65,536.
- [ ] The send loop drains queued frames into one pooled buffer, up to the
      byte limit, and writes once.
- [ ] A frame is never split. A frame larger than the limit is written alone.
- [ ] The interval coalesces frames that arrive during the wait. A send
      signal wakes the loop early.
- [ ] `Packets` counts frames and `Writes` counts socket writes. Issue 0058
      defined both. A batched exchange pins both.
- [ ] A test proves N frames produce one write at a nonzero interval.
- [ ] A test proves the byte limit is respected.
- [ ] A test proves a large frame is sent alone.
- [ ] A benchmark records syscall count and throughput before and after.
- [ ] The docs pass updates the TCP options tables and `performance`.
- [ ] No wire change.

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
