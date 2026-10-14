# Testing

One subject: how to test this library. Tests exist for safety. No flimsy
tests.

## Doctrine

- Test valid data, invalid data, and valid data becoming invalid. See the
  golden rule of assertions in [specs/coding-style](specs/coding-style.md).
- Test the boundaries. Minimum, -1, 0, 1, and maximum values. Empty and full
  buffers. Every offset.
- Test every error path. Most catastrophic failures come from mishandled
  errors. Every failure a caller can trigger gets a test.
- Keep tests deterministic. No sleeps, no wall-clock time, no dependence on
  thread scheduling. Inject time and random seeds.
- Test wire-format facts. Pin exact byte sizes and layouts. A drift in the
  encoding fails a test.
- Prove hot paths allocate nothing. Measure with
  `GC.GetAllocatedBytesForCurrentThread`.
- Use the always-on assertions as the fuzzing oracle. Fuzzing finds the bugs
  the assertions describe.
- Name tests by behavior. Example: `HandshakeRoundTripsBoundaryValues`.
- Use Arrange/Act/Assert. One test proves one behavior.
- Tests are the first consumer of the public API. Write them to be read.

## Structure

- One test class per type under test, in `NsdRpc.Tests/`.
- Shared helpers stay internal to the test project.

## What not to write

- No happy-path-only tests. A test that cannot fail proves nothing.
- No sleeps or retries for timing.
- No tests that depend on execution order or parallel workers.
- No tests of generated internals the schema already pins. Test the
  contract: round-trip, size, determinism.

## Current coverage

[NsdRpc.Tests/HandshakeTests.cs](../NsdRpc.Tests/HandshakeTests.cs:10) pins:

- round-trips for boundary values, spans, and offsets;
- deterministic output;
- the exact wire size of `Handshake`;
- zero allocation on the span round-trip;
- empty-buffer behavior.

Malformed-input tests are blocked by
[issue 0001](issues/0001-malformed-buffer-deserialize-overread.md). The
generated deserializers over-read truncated buffers.
