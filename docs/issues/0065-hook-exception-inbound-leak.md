# Bug: A dispatch hook exception leaks its inbound entry

- Type: bug
- Status: open
- Workflow: ../specs/issues.md

## Problem

`RpcPeer.DrainDispatchQueueAsync` catches an exception from the dispatch hook
and sends an `InternalError` response directly. It does not remove the inbound
entry from `_inbound`.

The entry holds a pending slot until the connection closes. Repeated hook
failures can exhaust `MaxPendingCalls`.

Issue [0064](0064-no-hook-inbound-leak.md) fixed the fallback path where no
hook is installed. It did not change the hook exception path.

## Acceptance criteria

- [ ] A dispatch hook exception sends `InternalError` and removes its inbound
      entry.
- [ ] A test proves the inbound table is empty after the error response.
