# Improvement: Connection metrics

- Type: improvement
- Status: open
- Workflow: ../specs/issues.md

## Problem

The transport exposes no traffic counters and no queue depth. A host needs
these values for an overlay and for load decisions.

- Frames and bytes in each direction.
- Queued bytes and the peak queue depth.
- Rejected sends.

## Acceptance criteria

- [ ] `RpcConnectionStats` is a readonly struct with `PacketsSent`,
      `PacketsReceived`, `BytesSent`, `BytesReceived`, `QueuedBytes`,
      `PeakQueuedBytes`, `Writes`, and `RejectedFrames`.
- [ ] `IRpcConnectionStats` is an optional capability with
      `RpcConnectionStats Stats { get; }`.
- [ ] `TcpConnection` implements it. `TcpClientConnection` and
      `TcpServerConnection` implement it and forward it. The counters use
      `Interlocked`.
- [ ] `BytesSent` and `BytesReceived` include the 4-byte length prefix.
      `Packets` counts frames and `Writes` counts socket writes. The two
      differ once batching lands in issue 0060. Both stay correct.
- [ ] `QueuedBytes` is the current outbound queue size. `PeakQueuedBytes` is
      the high-water mark since construction.
- [ ] `RejectedFrames` counts refused sends.
- [ ] `RpcPeer.Stats` returns the connection's stats. It returns defaults for
      a connection without the capability.
- [ ] A test pins exact counts for a known exchange.
- [ ] A test pins queued bytes and the peak under a stalled read.
- [ ] A test pins `Packets` and `Writes` for an exchange with more than one
      frame per write.
- [ ] A concurrent test proves no lost counts.
- [ ] The docs pass updates `quick-reference` and `rpc-api`.
- [ ] No wire change.

## Implementation notes

- Update counters once per frame at enqueue and dequeue. Do not update per
  byte. `Writes` increments once per completed socket send.
- Batching never counts a frame twice. `Packets` counts frames.
- The consumer expects packet and byte totals that match the peer's opposite
  counters. Include the prefix on both sides.
