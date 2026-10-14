# Bug: Deserialize reads out of bounds on truncated buffers

- Type: bug
- Status: done
- Workflow: ../specs/issues.md

## Problem

The nsdc-generated deserializers checked only the field header before they
read a value. `Handshake.Unpack` checked `offset + 2 < end`, then read a
4-byte int without a second check. A buffer of 3 to 5 bytes made the reader
read past the end of the buffer. The defect was in the nsdc compiler output.

## Resolution

The needlefish compiler fixed the defect in nsdc 1.3.3. The generated
readers now bound every field read. This repo pins the behavior with tests.
See [../../testing.md](../../testing.md).

## Acceptance criteria

- [x] Deserialize of every truncated buffer never reads out of bounds. A truncated field throws `InvalidDataException`. Empty input consumes 0 bytes. Every other outcome reports a consumed count within the buffer.
- [x] Tests cover every truncated length for every message in the schema. [NsdRpc.Tests/TruncatedBufferTests.cs](../../NsdRpc.Tests/TruncatedBufferTests.cs) pins `Handshake`. New messages add their own scans with the same helper.
