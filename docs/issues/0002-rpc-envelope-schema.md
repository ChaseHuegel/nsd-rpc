# Improvement: Add the RPC envelope schema and pin the wire contract

- Type: improvement
- Status: open
- Workflow: ../specs/issues.md

## Problem

The RPC wire protocol spec defines an envelope. No schema exists for it. The
wire contract has no tests. See
[../../specs/rpc-protocol.md](../../specs/rpc-protocol.md).

## Acceptance criteria

- [ ] `NsdRpc/Nsd/Rpc.nsd` matches the spec envelope exactly: fields, ids, types, optionality, and the `NsdRpc.Wire` namespace.
- [ ] `RpcMessageKind : byte` exists with `Unknown` 0, `Request` 1, `Response` 2, `Notification` 3, `Cancellation` 4.
- [ ] `RpcErrorCode` exists with the exact ratified values. App codes start at -32100.
- [ ] Wire tests pin the 26-byte minimum envelope and the 65,564-byte maximum envelope.
- [ ] Round-trip tests cover every kind and boundary values: `Id` and `Method` at 0, 1, and `ulong.MaxValue`.
- [ ] Tests pin null payload and null error omission. A null optional field is not encoded.
- [ ] Span round-trip tests prove zero allocation, same doctrine as HandshakeTests.
- [ ] Malformed-buffer tests exist for every truncated length. Issue 0001 is fixed first.
- [ ] The docs pass: [nsd-schemas](../../specs/nsd-schemas.md) gains the new schema, and [rpc-protocol](../../specs/rpc-protocol.md) gains `file:line` pointers.
