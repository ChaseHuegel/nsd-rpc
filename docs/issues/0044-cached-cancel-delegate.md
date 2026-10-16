# Improvement: Cache the call-cancelled delegate in RpcPeer

- Type: improvement
- Status: done
- Workflow: ../specs/issues.md

## Problem

`RpcPeer.InvokeAsync` registers a callback on the caller token:

```csharp
pending.Registration = cancellationToken.Register(OnCallCancelled, pending);
```

See [RpcPeer.cs:112](../../NsdRpc/Engine/RpcPeer.cs:112). The instance
method group allocates a delegate on every call. The stream overloads
repeat the pattern:
[RpcPeer.cs:177](../../NsdRpc/Engine/RpcPeer.cs:177),
[RpcPeer.cs:242](../../NsdRpc/Engine/RpcPeer.cs:242).

The delegate holds no per-call state. The pending call arrives as the state
argument. One delegate in a field serves every call.

## Acceptance criteria

- [x] `RpcPeer` creates the callback delegate once and reuses it for every registration.
- [x] A test measures the allocations of a warmed `InvokeAsync` call with a cancellable token and asserts the recorded bound.
- [x] The cancellation tests pass unchanged.
- [x] A token that fires between the add and the send still cancels the call. The existing race tests pass unchanged.

## Resolution

`RpcPeer` stores the `OnCallCancelled` delegate in a field and passes it to
every registration. The allocation test measures a warmed call with a
cancellable token and asserts a bound of 1,536 bytes. The registration node
is the tolerated allocation.
