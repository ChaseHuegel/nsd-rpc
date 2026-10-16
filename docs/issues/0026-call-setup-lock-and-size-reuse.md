# Improvement: Reuse locks and payload sizes in call setup

- Type: improvement
- Status: open
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

- [ ] Call setup takes at most one `_pendingLock` acquisition to allocate the id and add the pending call.
- [ ] The send happens outside the lock. A call that is removed before the send does not send.
- [ ] Ids stay monotonic and are never reused. A failed add still consumes the id. The overflow fault rule does not change.
- [ ] `GetSize` runs once per payload on the send path.
- [ ] A test covers the cancel-before-send race.
- [ ] Existing peer and streaming tests pass unchanged.
