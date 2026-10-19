# Bug: Map every payload decode failure to InvalidParams

- Type: bug
- Status: done
- Workflow: ../specs/issues.md

## Problem

`NsdPayloadSerializer.TryDeserialize` catches only `InvalidDataException`
([NsdPayloadSerializer.cs:83](../../NsdRpc/Engine/NsdPayloadSerializer.cs:83)).
An nsd unpack can throw other exceptions on a malformed payload. The
dispatcher then catches the exception as a handler failure and answers
`InternalError`.

A corrupt request looks like a handler bug. A corrupt notification logs as a
handler error. The failure is worse on the chunk and response paths. The
exception escapes the peer into the transport receive loop, and the TCP
transport treats it as a socket failure: the connection closes. A corrupt
chunk can kill the connection.

## Acceptance criteria

- [ ] Any exception from the payload deserializer maps to a decode failure.
- [ ] A request payload failure answers `InvalidParams`.
- [ ] A notification payload failure is dropped and logged.
- [ ] A chunk payload failure follows the documented stream rule. State the
      rule in `rpc-api` and test it.
- [ ] The connection stays open.
- [ ] The fix restores the documented `TryDeserialize` contract from
      `rpc-api`: it never throws.
- [ ] The wire carries no exception detail.
- [ ] Tests cover a truncated payload, a length that exceeds the buffer, and a
      valid frame with a corrupt payload.
- [ ] The docs pass updates `rpc-api` and the `rpc-protocol` error table.
- [ ] No wire change.

## Implementation notes

- Do not catch `OutOfMemoryException` or `StackOverflowException`. Catch the
  decode boundary only.
- The library must not depend on a specific nsdc exception type.
- Coordinate with the downstream nsdc unpack safety work. The library stays
  correct even when the generated reader throws an index error.
