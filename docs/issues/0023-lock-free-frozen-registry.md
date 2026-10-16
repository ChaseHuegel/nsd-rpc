# Improvement: Read the frozen handler registry without a lock

- Type: improvement
- Status: open
- Workflow: ../specs/issues.md

## Problem

`RpcRegistry.TryGet` takes `_lock` for every lookup:
[NsdRpc/Handlers/RpcRegistry.cs:147](../../NsdRpc/Handlers/RpcRegistry.cs:147).
Every dispatch calls `TryGet`. The registry snapshot is immutable after
`Freeze`. The lock does no useful work on the read path.

If the host never calls `Freeze`, the first lookup builds the snapshot under
the lock. Later lookups still take the lock, although the snapshot never
changes. On a server with many peers, all dispatch paths contend on one
registry lock.

## Acceptance criteria

- [ ] `TryGet` reads a published snapshot without `_lock` after publication.
- [ ] `Freeze` publishes the snapshot. The first lookup publishes the snapshot when the host never calls `Freeze`.
- [ ] `Add` after `Freeze` still throws. A duplicate id still throws.
- [ ] A test covers a lookup before `Freeze`, a lookup after `Freeze`, and an unknown method id.
- [ ] A test proves repeated `TryGet` calls allocate nothing.
- [ ] Existing registry, dispatcher, and server tests pass unchanged.
