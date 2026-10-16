# Improvement: Build no drop string while the trace switch is off

- Type: improvement
- Status: open
- Workflow: ../specs/issues.md

## Problem

`RpcLog.Drop` formats a message and calls `TraceSource.TraceEvent`:
[RpcLog.cs:18](../../NsdRpc/Engine/RpcLog.cs:18). The default switch level
is `Off`. The event is not delivered. The caller still builds the string.

`TraceSource.TraceEvent` checks the switch. It checks it after the caller
evaluates the arguments. A measurement of the same call shape records about
104 bytes per drop with the switch off. `TRACE` is defined in Debug and
Release builds, so the conditional method does not remove the work.

Drop paths are reachable from the network. A hostile peer can make the
receiver allocate one string per frame, for example through an unknown
message kind:
[RpcPeer.cs:344](../../NsdRpc/Engine/RpcPeer.cs:344).

## Acceptance criteria

- [ ] `RpcLog` checks `Source.Switch.ShouldTrace` before it builds a drop or error message.
- [ ] Call sites with dynamic text, such as `"unknown message kind " + envelope.Kind`, build the text only when the switch allows the event.
- [ ] A test with the switch off proves a drop allocates nothing on the calling thread. Use `GC.GetAllocatedBytesForCurrentThread`.
- [ ] A test with the switch on and a listener attached proves the event arrives with the same reason text.
- [ ] `RpcLog.Error` gets the same guard.
