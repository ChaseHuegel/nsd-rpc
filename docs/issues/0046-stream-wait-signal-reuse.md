# Improvement: Reuse the stream consumer signal instead of a TCS per park

- Type: improvement
- Status: done
- Workflow: ../specs/issues.md

## Problem

`AsyncChunkQueue<T>.EnumerateAll` creates a `TaskCompletionSource<bool>`
whenever the consumer waits on an empty queue:
[AsyncChunkQueue.cs:65](../../NsdRpc/Engine/AsyncChunkQueue.cs:65). Each
chunk burst that drains the queue allocates one signal object.

`RpcStreamSession` also starts one async state machine per session to
complete the queue:
[RpcStreamSession.cs:24](../../NsdRpc/Engine/RpcStreamSession.cs:24).

The queue has one consumer by contract. One reusable wait source per
session removes the per-park allocation. `ManualResetValueTaskSourceCore`
is a candidate. The async enumeration allocates its own state machine.
State the tolerated allocations.

## Acceptance criteria

- [x] The wait path reuses one wait source. A warm chunk wait allocates no signal object.
- [x] A test measures the allocations of a warmed chunk wait and asserts the recorded bound.
- [x] The overflow, completion, and cancellation tests pass unchanged.
- [x] The docs pass updates [rpc-api](../specs/rpc-api.md) if the stream contract changes.

## Resolution

`AsyncChunkQueue<T>` implements `IValueTaskSource` over one
`ManualResetValueTaskSourceCore<bool>`. A park awaits a `ValueTask` over the
queue, so it allocates no signal. `ParkingTheConsumerDoesNotAllocateASignal`
asserts zero allocation.

The stream contract does not change, so [rpc-api](../specs/rpc-api.md)
needs no update.
