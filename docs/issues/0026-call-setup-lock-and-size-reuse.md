# Improvement: Reuse locks and payload sizes in call setup

- Type: improvement
- Status: done
- Workflow: ../specs/issues.md

## Problem

`RpcPeer.InvokeAsync` takes `_pendingLock` three times per call:

1. `TryAllocateId` at [RpcPeer.cs:86](../../NsdRpc/Engine/RpcPeer.cs:86)
2. `TryAddPending` at [RpcPeer.cs:104](../../NsdRpc/Engine/RpcPeer.cs:104)
3. `IsPending` at [RpcPeer.cs:115](../../NsdRpc/Engine/RpcPeer.cs:115)

The helpers live at [RpcPeer.cs:580](../../NsdRpc/Engine/RpcPeer.cs:580),
[RpcPeer.cs:627](../../NsdRpc/Engine/RpcPeer.cs:627), and
[RpcPeer.cs:641](../../NsdRpc/Engine/RpcPeer.cs:641). Both stream overloads
repeat the pattern.

`RpcPeer.SerializePayload` computes the payload size, then
`NsdPayloadSerializer.Serialize` computes it again:
[RpcPeer.cs:721](../../NsdRpc/Engine/RpcPeer.cs:721) and
[NsdPayloadSerializer.cs:67](../../NsdRpc/Engine/NsdPayloadSerializer.cs:67).
The second call is pure CPU work.

## Acceptance criteria

- [x] Call setup takes at most one `_pendingLock` acquisition to allocate the id and add the pending call.
- [x] The send happens outside the lock. A call that is removed before the send does not send.
- [x] Ids stay monotonic and are never reused. A failed add still consumes the id. The overflow fault rule does not change.
- [x] `GetSize` runs once per payload on the send path.
- [x] A test covers the cancel-before-send race.
- [x] Existing peer and streaming tests pass unchanged.

## Resolution

`TryAddPendingCall` allocates the id and adds the pending call under one
`_pendingLock` acquisition. The caller constructs the pending call with id 0
and the method assigns the id. Serialization runs outside the lock.

`OnCallCancelled` sets the pending `Cancelled` flag. The send path checks the
flag instead of taking the lock again, so a call cancelled before the send
does not send its request.

`IPayloadSerializer<T>` gained a `Serialize` overload that takes the size
from `GetSize`. The peer computes the size once and passes it.
