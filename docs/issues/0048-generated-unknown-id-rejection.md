# Bug: The generated reader cannot reject an unknown field id among known ids

- Type: bug
- Status: open
- Workflow: ../specs/issues.md

## Problem

The nsd wire format writes each field as a `ushort` id and a value. A value
has no length prefix, so a reader can only skip a field it knows.

The needlefish-generated reader scans field ids until it reads every field it
knows. An unknown id consumes only its own two bytes. The next two bytes are
the unknown value, and the reader treats them as an id. A frame that places a
new id before a known id can misparse a known field or throw.

The RPC layer cannot fix this. The generated code is upstream, and the repo
does not patch it. The precedent is issue 0001 and issue 0027.

The protocol rule now states the real behavior. An appended unknown field is
ignored because the reader stops after the known fields. An unknown id among
known ids stays a silent corruption risk.

## Acceptance criteria

- [ ] The needlefish reader rejects an unknown field id with a clear error or skips the whole field. The fix ships in a released package.
- [ ] The `Needlefish.Generators` pin moves after the release.
- [ ] A test places an unknown id before a known field and proves the reader rejects the frame or keeps the known fields intact.
- [ ] The wire bytes do not change.
