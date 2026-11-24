# Bug: Tests race the global RpcLog trace switch

- Type: bug
- Status: done
- Workflow: ../specs/issues.md

## Problem

`DroppedFrameDoesNotAllocateWhenTracingIsOff` asserts that 100
`RpcPeer.HandleFrame` calls allocate zero bytes on the current thread:
[RpcLogTests.cs:26](../../NsdRpc.Tests/RpcLogTests.cs:26). The test shares the
process with other test classes, and xunit runs test classes in parallel.
`PayloadDecodeTests` and two tests in `RpcLogTests` set the static
`RpcLog.Source.Switch.Level` to `Warning` and add a listener:
[PayloadDecodeTests.cs:132](../../NsdRpc.Tests/PayloadDecodeTests.cs:132).
While that switch is on, `RpcLog.Drop` formats and writes a message, so each
dropped frame allocates.

The first publish run for `v0.3.2` failed on this assertion: expected 184928
allocated bytes, actual 1428728. The build and test workflows for the same
commit passed, and the next publish run passed. `NsdRpc.Tests` had no
`xunit.runner.json` and no collection that disables parallelization.

## Acceptance criteria

- [x] No test changes `RpcLog.Source` while the allocation test runs.
- [x] The allocation test still fails when `RpcLog.Drop` allocates with tracing
  off.
- [x] Every test that changes `RpcLog.Source` restores the previous switch
  level and listener set.

## Resolution

`NsdRpc.Tests` disables test collection parallelization:
[AssemblyInfo.cs:6](../../NsdRpc.Tests/AssemblyInfo.cs:6). The process-global
switch and listener list then cannot change under an allocation test.

`RpcLogScope` snapshots the switch level and the listener set, then restores
both when the test ends: [RpcLogScope.cs:7](../../NsdRpc.Tests/RpcLogScope.cs:7).
Every test that changes `RpcLog.Source` uses the scope.

The allocation test now sends a short frame. That frame reaches `RpcLog.Drop`
without a caller guard, so an allocation inside `Drop` with the switch off
fails the test. A second test,
`UnknownKindFrameDoesNotBuildTheDropTextWhenTracingIsOff`, covers the guarded
unknown-kind text. `DroppedFrameAllocatesWhenTracingIsOn` is the control. It
proves the measurement loop reaches the drop path and that it detects the trace
write.
