# Improvement: Add RpcPeer, the protocol engine

- Type: improvement
- Status: done
- Workflow: ../specs/issues.md

## Problem

The protocol needed one engine per connection. The engine owns the envelope,
the id space, the pending calls, timeouts, cancellation, and faults. The
specs are [../../specs/rpc-protocol.md](../../specs/rpc-protocol.md) and
[../../specs/rpc-api.md](../../specs/rpc-api.md).

## Acceptance criteria

- [x] The id counter starts at 1 and uses checked arithmetic. Overflow faults the connection. Ids are never reused. The overflow branch is unreachable in practice and not directly tested.
- [x] `InvokeAsync` returns `Task<RpcResult<TResponse>>`. `Notify` returns bool. Both `Notify` overloads exist.
- [x] `MaxPendingCalls` bounds the pending table and the inbound set. A full table fails a new call with `Busy` before it sends. A full inbound set answers a new request with `Busy`.
- [x] A response completes its call by id. Order does not matter. Unknown ids, duplicate responses, and responses to notifications are dropped and logged. None of these assert.
- [x] The default timeout comes from `RpcOptions`. A per-call timeout overrides it. Zero disables the deadline. Tests inject a fake `TimestampNanos`. No test sleeps.
- [x] A token firing sends a cancellation frame, removes the entry, and completes the call with `Cancelled`.
- [x] A deadline sends a cancellation frame, removes the entry, and completes the call with `RequestTimeout`.
- [x] A failed `TrySend` completes the call with `Unavailable` and leaves no entry.
- [x] A received cancellation marks the inbound id cancelled. An undispatched request is never dispatched. A running handler finishes, but the peer drops its response and chunks at send. No ack.
- [x] `FaultPending` completes every pending call with the given error. A test proves the table is empty after.
- [x] `HandleFrame` validates `Version` and `Kind` before routing. Invalid frames are dropped and logged.
- [x] A duplicate inbound request id is dropped before dispatch.
- [x] Streaming bookkeeping: a chunk resets the deadline. The pending entry holds the chunk sink. One pump processes frames in arrival order.
- [x] `CheckTimeouts(long)` completes expired entries. `Poll` checks deadlines with the current timestamp.
- [x] An in-memory transport pair and a fake clock land in the test project. Every rule above has a test. No happy-path-only tests.
- [x] The peer knows nothing about handlers. Dispatch goes through a minimal internal hook. The full dispatcher is issue 0005.
- [x] The docs pass: [rpc-api](../../specs/rpc-api.md) gains `file:line` pointers, and [architecture](../../architecture.md) gains the module row.
