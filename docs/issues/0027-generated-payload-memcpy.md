# Improvement: Generated codecs copy payloads byte by byte

- Type: improvement
- Status: open
- Workflow: ../specs/issues.md

## Problem

The needlefish-generated `Envelope` codec copies the payload `byte[]` one
byte at a time. `SerializeInto` runs the copy loop on the send path.
`Unpack` runs a matching loop on the receive path. The loop runs on
little-endian machines, where the bytes need no swap.

A full payload is 65,535 bytes. One message copies it twice with 65,535
iterations per copy. A bulk copy does the same work with one call. The
payload cap is in [nsd-schemas](../specs/nsd-schemas.md).

The defect is in the generator output. This repository does not patch
generated code. The precedent is issue 0001.

## Acceptance criteria

- [ ] Upstream needlefish emits a bulk copy for `byte[]` fields on little-endian machines. The fix ships in a released package.
- [ ] The `Needlefish.Generators` pin in [NsdRpc.csproj:40](../../NsdRpc/NsdRpc.csproj:40) and [Chat.Common.csproj:16](../../samples/Chat.Common/Chat.Common.csproj:16) moves together after the release.
- [ ] A round-trip test with a maximum-size payload pins the wire bytes.
- [ ] The wire bytes do not change. The wire-contract tests pass unchanged.
- [ ] A measurement records the copy cost of a maximum-size payload before and after the bump. Record the numbers in this issue.
