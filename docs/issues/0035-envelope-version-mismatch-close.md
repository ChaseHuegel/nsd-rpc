# Bug: Envelope version mismatch does not close the connection

- Type: bug
- Status: open
- Workflow: ../specs/issues.md

## Problem

The protocol doc says a receiver with another version drops the frame and
closes the connection ([rpc-protocol.md:200](../specs/rpc-protocol.md)).
`RpcPeer.HandleFrame` faults every pending call instead and ignores all later
frames ([RpcPeer.cs:322](../../NsdRpc/Engine/RpcPeer.cs:322)). Nothing closes
the transport connection. The socket and its buffers stay open until the peer
or the host closes them.

`IRpcConnection` has no close operation, so the peer cannot close the
transport by contract today.

## Acceptance criteria

- [ ] A version mismatch ends the connection, or the doc states the fault-only behavior.
- [ ] The chosen behavior has a test.
- [ ] The dead connection does not leak resources.
