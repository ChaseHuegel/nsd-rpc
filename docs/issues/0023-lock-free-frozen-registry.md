# Improvement: Read the frozen handler registry without a lock

- Type: improvement
- Status: done
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

- [x] `TryGet` reads a published snapshot without `_lock` after publication.
- [x] `Freeze` publishes the snapshot. The first lookup publishes the snapshot when the host never calls `Freeze`.
- [x] `Add` after `Freeze` still throws. A duplicate id still throws.
- [x] A test covers a lookup before `Freeze`, a lookup after `Freeze`, and an unknown method id.
- [x] A test proves repeated `TryGet` calls allocate nothing.
- [x] Existing registry, dispatcher, and server tests pass unchanged.

## Resolution

`TryGet` reads a `volatile` snapshot field without the lock. The first
lookup publishes the snapshot under the lock. `Freeze` publishes it too. Each
`Add` clears the field, so an add before `Freeze` invalidates an earlier
publication.
