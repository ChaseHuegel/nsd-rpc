# Bug: Deserialize reads out of bounds on truncated buffers

- Type: bug
- Status: open
- Workflow: ../specs/issues.md

## Problem

The nsdc-generated deserializers check only the field header before they read
a value. `Handshake.Unpack` checks `offset + 2 < end`, then reads a 4-byte
int without a second check. A buffer of 3 to 5 bytes makes the reader read
past the end of the buffer. This is an out-of-bounds read on malformed input.
The defect is in the nsdc compiler output, so the fix belongs in the
needlefish compiler or in a hardening wrapper. Do not hand-patch generated
code.

Tests cannot cover malformed input until this is fixed. See
[../../testing.md](../../testing.md).

## Acceptance criteria

- [ ] Deserialize of every truncated buffer length returns 0 consumed and a default value without reading out of bounds.
- [ ] Tests cover every truncated length for every message in the schema.
