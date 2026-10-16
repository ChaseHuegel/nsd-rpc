# Improvement: Skip timeout scans with a per-peer deadline watermark

- Type: improvement
- Status: done
- Workflow: ../specs/issues.md

## Problem

`RpcPeer.CheckTimeouts` iterates the whole pending table and takes
`_pendingLock`:
[RpcPeer.cs:349](../../NsdRpc/Engine/RpcPeer.cs:349). `RpcServer.Poll`
calls it for every peer:
[RpcServer.cs:59](../../NsdRpc/RpcServer.cs:59).

The total cost is the number of peers times the number of calls per peer. A
server with 10,000 peers and 1,000 calls per peer scans 10 million entries
per poll. Most polls expire nothing.

## Acceptance criteria

- [x] `RpcPeer` tracks the earliest deadline in the pending table, or an equivalent watermark.
- [x] `CheckTimeouts` returns before it takes `_pendingLock` when the timestamp is below the watermark.
- [x] A call add, a call remove, and a chunk deadline reset update the watermark.
- [x] A test proves a poll below the watermark skips the table scan. Add an internal observable if needed. The test states what it measures.
- [x] A test proves each deadline transition still expires at the exact timestamp. The fake clock from the current timeout tests covers it.
- [x] The timeout and fault tests pass unchanged.

## Resolution

`CheckTimeouts` returns before the lock when the timestamp is below
`_earliestDeadlineNanos` and no change is pending. Every add, remove, and
chunk deadline reset sets `_deadlinesDirty`. A scan recomputes the earliest
deadline from the entries that remain. `TimeoutScanCount` lets the test prove
that a poll below the watermark does not scan.
