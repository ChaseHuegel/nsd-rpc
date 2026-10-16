# Bug: A notification flood grows the dispatch queue without a bound

- Type: bug
- Status: done
- Workflow: ../specs/issues.md

## Problem

`RpcPeer.HandleStandaloneNotification` enqueues every notification with no
bound check:
[RpcPeer.cs:956](../../NsdRpc/Engine/RpcPeer.cs:956).

A request is bounded. `HandleRequest` checks `_inbound.Count` against
`MaxPendingCalls` before it enqueues:
[RpcPeer.cs:864](../../NsdRpc/Engine/RpcPeer.cs:864). A notification makes
no inbound entry. It never reaches that check.

One worker drains the queue. A slow handler plus a notification flood grows
the queue without a limit. Each entry holds its payload array, up to 65,535
bytes:
[DispatchEntry.cs:17](../../NsdRpc/Engine/DispatchEntry.cs:17). The peer
runs out of memory.

The rule "Put a limit on everything" lives in
[coding-style](../specs/coding-style.md).

## Acceptance criteria

- [x] A bound applies to the dispatch queue. The bound is a config key in `RpcOptions` or the existing `MaxPendingCalls`.
- [x] A full queue drops the notification and logs the drop. A request at capacity still answers `Busy` as today.
- [x] A test blocks the worker, floods notifications, and proves the queue never exceeds the bound.
- [x] A test proves the peer still dispatches requests and notifications after the queue drains.
- [x] The docs pass records the bound and its default in [rpc-api](../specs/rpc-api.md).

## Resolution

`MaxPendingCalls` bounds the dispatch queue. `HandleStandaloneNotification`
drops the notification and logs the drop when the queue is full. The
[rpc-api](../specs/rpc-api.md) rule records the bound.
