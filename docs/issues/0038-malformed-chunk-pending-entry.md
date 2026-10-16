# Bug: A malformed chunk completes the call and keeps the pending entry

- Type: bug
- Status: open
- Workflow: ../specs/issues.md

## Problem

`PendingStreamCall.ReceiveChunk` fails the call when the chunk payload does
not decode
([PendingStreamCall.cs:36](../../NsdRpc/Engine/PendingStreamCall.cs:36)). It
does not remove the pending entry. The caller sees `InvalidRequest`, the
pending table still counts the call, and a later deadline check sends a
cancellation frame for a call that already finished. The entry holds a slot
against `MaxPendingCalls` until a response, a timeout, or a disconnect.

`HandleResponse` removes the entry before it completes the call. The chunk
path must do the same.

## Acceptance criteria

- [ ] A failed chunk decode removes the pending entry.
- [ ] A test with a full pending table proves that the slot is free after the failure.
- [ ] No cancellation frame follows a call that already completed.
