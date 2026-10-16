# Bug: default(RpcResult<T>).ErrorMessage is null

- Type: bug
- Status: open
- Workflow: ../specs/issues.md

## Problem

`RpcResult<T>.ErrorMessage` is declared as a non-nullable `string`, and the
doc says it is empty on success
([RpcResult.cs:28](../../NsdRpc/Engine/RpcResult.cs:28)). A default struct
reports `IsSuccess` true and returns null for `ErrorMessage`. A consumer that
calls a member on the message can fail with a null reference. A
zero-initialized result is the only source of the null value.

## Acceptance criteria

- [ ] A default result returns an empty message, or the contract states the null case.
- [ ] A test pins the chosen behavior.
