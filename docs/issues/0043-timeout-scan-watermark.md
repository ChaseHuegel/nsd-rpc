# Improvement: Skip timeout scans with a per-peer deadline watermark

- Type: improvement
- Status: open
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

- [ ] `RpcPeer` tracks the earliest deadline in the pending table, or an equivalent watermark.
- [ ] `CheckTimeouts` returns before it takes `_pendingLock` when the timestamp is below the watermark.
- [ ] A call add, a call remove, and a chunk deadline reset update the watermark.
- [ ] A test proves a poll below the watermark skips the table scan. Add an internal observable if needed. The test states what it measures.
- [ ] A test proves each deadline transition still expires at the exact timestamp. The fake clock from the current timeout tests covers it.
- [ ] The timeout and fault tests pass unchanged.
