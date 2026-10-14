# Improvement: Add the RPC envelope schema and pin the wire contract

- Type: improvement
- Status: done
- Workflow: ../specs/issues.md

## Problem

The RPC wire protocol spec defined an envelope. No schema existed for it. The
wire contract had no tests. See
[../../specs/rpc-protocol.md](../../specs/rpc-protocol.md).

## Acceptance criteria

- [x] `NsdRpc/Nsd/Rpc.nsd` matches the spec envelope exactly: fields, ids, types, optionality, and the `NsdRpc.Wire` namespace.
- [x] `RpcMessageKind : byte` exists with `Unknown` 0, `Request` 1, `Response` 2, `Notification` 3, `Cancellation` 4.
- [x] `RpcErrorCode` exists with the exact ratified values. App codes start at -32100.
- [x] Wire tests pin the 26-byte minimum envelope and the 65,566-byte maximum envelope.
- [x] Round-trip tests cover every kind and boundary values: `Id` and `Method` at 0, 1, and `ulong.MaxValue`.
- [x] Tests pin null payload and null error omission. A null optional field is not encoded.
- [x] Span round-trip tests prove zero allocation, same doctrine as HandshakeTests.
- [x] Malformed-buffer tests exist for every truncated length. The TruncatedBufferHelper from issue 0001 scans each message.
- [x] The docs pass: [nsd-schemas](../../specs/nsd-schemas.md) gains the new schema, and [rpc-protocol](../../specs/rpc-protocol.md) gains `file:line` pointers.
