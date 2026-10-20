# Bug: Canceled inbound entries keep a disposed token source

- Type: bug
- Status: done
- Workflow: ../specs/issues.md

## Problem

`RpcDispatcher` creates a `CancellationTokenSource` for each inbound request
and attaches it to the inbound entry. The dispatcher disposes the source when
the handler returns.

A cancellation marks the inbound entry cancelled. `RpcPeer.ClaimInbound`
returned `Cancelled` without removing the entry. The entry therefore kept the
disposed source. `RpcPeer.FaultPending` runs on disconnect and calls `Cancel()`
on every inbound source. A disposed source throws `ObjectDisposedException`.

The exception escaped the transport's `Disconnected` event. It also stopped
`FaultPending` before it failed the pending calls. A canceled entry kept a
pending slot until the connection closed. Enough canceled calls could exhaust
`MaxPendingCalls`.

A canceled request that was still queued for dispatch leaked its entry too.
A late cancellation could also throw from `HandleCancellation` when it found a
disposed source.

## Acceptance criteria

- [x] A canceled request leaves no entry after its terminal response or
      failure, or after the dispatcher skips it before dispatch.
- [x] A response or failure after cancellation removes the entry.
- [x] `FaultPending` and `HandleCancellation` tolerate a token source that is
      already disposed.
- [x] A test cancels a call, waits for the handler to return, then disconnects.
      The disconnect raises no exception and pending calls still fault.
- [x] The fix ships in package version 0.3.1.
