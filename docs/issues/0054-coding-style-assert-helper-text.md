# Bug: Coding style says the assert helper does not exist

- Type: bug
- Status: done
- Workflow: ../specs/issues.md

## Problem

`docs/specs/coding-style.md` says the always-on assert helper does not exist
and that the first library code change must add it
([coding-style.md:50](../specs/coding-style.md)). The helper exists as
`NsdRpc/Invariant.cs` and the library uses it. The text is stale.

## Acceptance criteria

- [x] The assertions section names `NsdRpc/Invariant.cs` and its methods.
- [x] It keeps the always-on rule.
- [x] No sentence in the section claims the helper is missing.

## Resolution

The assertions section now points at
[NsdRpc/Invariant.cs](../../NsdRpc/Invariant.cs:7) and names `Invariant.That`,
`Invariant.NotNull`, and `Invariant.Fail`. It keeps the always-on rule and no
sentence claims the helper is missing.
