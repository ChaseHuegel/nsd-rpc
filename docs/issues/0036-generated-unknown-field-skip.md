# Bug: The generated reader does not skip unknown fields

- Type: bug
- Status: done
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

- [x] The forward-compatibility rule is defined for a field with an unknown id.
- [x] The doc and the generated reader agree.
- [x] A test appends a field with a new id and a value that scans as a known
  id.
- [x] The reader either rejects the frame or keeps the known fields intact.
  The chosen behavior is documented.

## Resolution

The protocol rule now states the real behavior. Field ids are written in
ascending order. The reader stops after it reads every field it knows, so an
appended field with a new id is ignored. The nsd format has no field-length
prefix, so a reader cannot skip an unknown field that sits before a known
field.

The injected-id case needs a generated default case. That change is upstream.
It is filed as issue [0048](../issues/0048-generated-unknown-id-rejection.md).
