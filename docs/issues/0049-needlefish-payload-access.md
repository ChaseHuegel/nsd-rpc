# Improvement: Non-allocating payload access in needlefish

- Type: improvement
- Status: open
- Workflow: ../specs/issues.md

## Problem

Issue [0030](0030-allocation-free-payload-path.md) found that the payload
path cannot avoid the per-message array. The generated `Envelope` holds the
payload as a `byte[]`. The generated `Unpack` always allocates that array.
The generated `SerializeInto` always copies that array.

The library may not take the envelope apart by hand. It calls the generated
methods. So the fix is an upstream needlefish feature. This repository does
not patch generated code. The precedent is issue
[0027](0027-generated-payload-memcpy.md).

## Acceptance criteria

- [ ] Needlefish reports the payload range on receive without an array
  allocation. The released package ships the feature.
- [ ] Needlefish writes a payload from a span or a writer on send, so the
  caller builds the payload field in one pass.
- [ ] The `Needlefish.Generators` pin in
  [NsdRpc.csproj:40](../../NsdRpc/NsdRpc.csproj:40) and
  [Chat.Common.csproj:16](../../samples/Chat.Common/Chat.Common.csproj:16)
  moves together after the release.
- [ ] The wire bytes do not change. The wire-contract tests pass unchanged.
- [ ] A measurement records the receive payload allocation of one message
  before and after the bump. Record the numbers in this issue.
