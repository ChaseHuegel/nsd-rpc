# Bug: The generated reader does not skip unknown fields

- Type: bug
- Status: open
- Workflow: ../specs/issues.md

## Problem

The protocol doc says unknown fields are skipped
([rpc-protocol.md:203](../specs/rpc-protocol.md)). The generated reader has no
default case. It reads an unknown field id, consumes nothing for the value,
and continues to scan the value bytes as field ids. A future field value that
starts with a known id can corrupt the read or throw.

`EnvelopeIgnoresTrailingUnknownFields` passes because its bytes do not
collide with a known id
([EnvelopeTests.cs:112](../../NsdRpc.Tests/EnvelopeTests.cs:112)).

## Acceptance criteria

- [ ] The forward-compatibility rule is defined for a field with an unknown id.
- [ ] The doc and the generated reader agree.
- [ ] A test appends a field with a new id and a value that scans as a known
  id.
- [ ] The reader either rejects the frame or keeps the known fields intact.
  The chosen behavior is documented.
