# Bug: A request without a dispatch hook leaks its inbound entry

- Type: bug
- Status: open
- Workflow: ../specs/issues.md

## Problem

`RpcPeer.DrainDispatchQueueAsync` sends a `MethodNotFound` response directly
when no dispatch hook is installed. It does not remove the inbound entry from
`_inbound`.

The entry has no cancellation source, so a disconnect does not throw. The
entry still holds a pending slot until the connection closes. Repeated
requests without a hook can exhaust `MaxPendingCalls`.

Issue [0063](0063-canceled-inbound-disposed-source.md) fixed the canceled
entry leak. It did not change this path.

## Acceptance criteria

- [ ] A request with no dispatch hook sends `MethodNotFound` and removes its
      inbound entry.
- [ ] A test proves the inbound table is empty after the fallback response.
