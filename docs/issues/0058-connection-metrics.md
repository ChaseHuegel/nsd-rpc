# Improvement: Connection metrics

- Type: improvement
- Status: done
- Workflow: ../specs/issues.md

## Problem

The transport exposes no traffic counters and no queue depth. A host needs
these values for an overlay and for load decisions.

- Frames and bytes in each direction.
- Queued bytes and the peak queue depth.
- Rejected sends.

## Acceptance criteria

- [x] `RpcConnectionStats` is a readonly struct with `PacketsSent`,
      `PacketsReceived`, `BytesSent`, `BytesReceived`, `QueuedBytes`,
      `PeakQueuedBytes`, `Writes`, and `RejectedFrames`.
- [x] `IRpcConnectionStats` is an optional capability with
      `RpcConnectionStats Stats { get; }`.
- [x] `TcpConnection` implements it. `TcpClientConnection` and
      `TcpServerConnection` implement it and forward it. The counters use
      `Interlocked`.
- [x] `BytesSent` and `BytesReceived` include the 4-byte length prefix.
      `Packets` counts frames and `Writes` counts socket writes. The two
      differ once batching lands in issue 0060. Both stay correct.
- [x] `QueuedBytes` is the current outbound queue size. `PeakQueuedBytes` is
      the high-water mark since construction.
- [x] `RejectedFrames` counts refused sends.
- [x] `RpcPeer.Stats` returns the connection's stats. It returns defaults for
      a connection without the capability.
- [x] A test pins exact counts for a known exchange.
- [x] A test pins queued bytes and the peak under a stalled read.
- [x] A test pins `Writes` at or above `Packets` for a normal exchange.
      Issue 0060 pins the more-than-one-frame-per-write case.
- [x] A concurrent test proves no lost counts.
- [x] The docs pass updates `quick-reference` and `rpc-api`.
- [x] No wire change.

## Implementation notes

- Update counters once per frame at enqueue and dequeue. Do not update per
  byte. `Writes` increments once per completed socket send.
- Batching never counts a frame twice. `Packets` counts frames.
- The consumer expects packet and byte totals that match the peer's opposite
  counters. Include the prefix on both sides.

## Resolution

`RpcConnectionStats` holds the counters. `IRpcConnectionStats` is an optional
capability. `TcpConnection` counts a frame at the send drain and at the
receive push, counts each socket write, and tracks the queue peak under the
send lock. `TcpClientConnection` and `TcpServerConnection` forward the
capability. `RpcPeer.Stats` returns zeros when the transport has none. The
handshake is excluded from the counters.
