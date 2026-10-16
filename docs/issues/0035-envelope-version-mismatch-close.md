# Bug: Envelope version mismatch does not close the connection

- Type: bug
- Status: done
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

- [x] A version mismatch ends the connection, or the doc states the fault-only behavior.
- [x] The chosen behavior has a test.
- [x] The dead connection does not leak resources.

## Resolution

A version mismatch faults every pending call and closes the connection.
`IRpcConnection` gained `Close`. The TCP server and client connections
implement it, and so do the test and benchmark connections. The protocol doc
keeps the close rule.
