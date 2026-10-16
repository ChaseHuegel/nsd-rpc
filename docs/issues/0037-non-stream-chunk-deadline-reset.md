# Bug: A chunk for a non-stream call resets its deadline

- Type: bug
- Status: done
- Workflow: ../specs/issues.md

## Problem

`RpcPeer.HandleChunk` resets the pending deadline before it asks the call to
consume the chunk
([RpcPeer.cs:987](../../NsdRpc/Engine/RpcPeer.cs:987)). `PendingCallBase.ReceiveChunk`
returns false for a non-stream call, and the peer then drops the chunk. The
deadline already moved. Any notification with a matching id extends the
deadline of a non-stream call. Stray notifications can keep a call alive past
its configured timeout.

## Acceptance criteria

- [x] The deadline resets only when the call consumes the chunk.
- [x] A test with a fake clock proves that a dropped chunk does not extend the deadline.

## Resolution

`HandleChunk` asks the call to consume the chunk first. It resets the
deadline only after the call takes the chunk.
