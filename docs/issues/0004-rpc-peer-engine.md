# Improvement: Add RpcPeer, the protocol engine

- Type: improvement
- Status: open
- Workflow: ../specs/issues.md

## Problem

The protocol needs one engine per connection. The engine owns the envelope,
the id space, the pending calls, timeouts, cancellation, and faults. The
specs are [../../specs/rpc-protocol.md](../../specs/rpc-protocol.md) and
[../../specs/rpc-api.md](../../specs/rpc-api.md).

## Acceptance criteria

- [ ] The id counter starts at 1 and uses checked arithmetic. Overflow faults the connection. Ids are never reused.
- [ ] `InvokeAsync` returns `Task<RpcResult<TResponse>>`. `Notify` returns bool. Both `Notify` overloads exist.
- [ ] `MaxPendingCalls` bounds the pending table and the inbound set. A full table fails a new call with `Busy` before it sends. A full inbound set answers a new request with `Busy`.
- [ ] A response completes its call by id. Order does not matter. Unknown ids, duplicate responses, and responses to notifications are dropped and logged. None of these assert.
- [ ] The default timeout comes from `RpcOptions`. A per-call timeout overrides it. Zero disables the deadline. Tests inject a fake `TimestampNanos`. No test sleeps.
- [ ] A token firing sends a cancellation frame, removes the entry, and completes the call with `Cancelled`.
- [ ] A deadline sends a cancellation frame, removes the entry, and completes the call with `RequestTimeout`.
- [ ] A failed `TrySend` completes the call with `Unavailable` and leaves no entry.
- [ ] A received cancellation marks the inbound id cancelled. An undispatched request is never dispatched. A running handler finishes, but the peer drops its response and chunks at send. No ack.
- [ ] `FaultPending` completes every pending call with the given error. A test proves the table is empty after.
- [ ] `HandleFrame` validates `Version` and `Kind` before routing. Invalid frames are dropped and logged.
- [ ] A duplicate inbound request id is dropped before dispatch.
- [ ] Streaming bookkeeping: a chunk resets the deadline. The pending entry holds the chunk sink. One pump processes frames in arrival order.
- [ ] `CheckTimeouts(long)` completes expired entries. `Poll` drains a pull transport and then checks deadlines.
- [ ] An in-memory transport pair and a fake clock land in the test project. Every rule above has a test. No happy-path-only tests.
- [ ] The peer knows nothing about handlers. Dispatch goes through a minimal internal hook. The full dispatcher is issue 0005.
- [ ] The docs pass: [rpc-api](../../specs/rpc-api.md) gains `file:line` pointers, and [architecture](../../architecture.md) gains the module row.
