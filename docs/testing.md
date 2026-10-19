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
- No tests for test fixtures. The tests that use a fake prove the fake.
- No sleeps or retries for timing.
- No tests that depend on execution order or parallel workers.
- No tests of generated internals the schema already pins. Test the
  contract: round-trip, size, determinism.

## Current coverage

The wire tests pin the generated types against the contract in
[nsd-schemas](specs/nsd-schemas.md):

- [HandshakeTests.cs](../NsdRpc.Tests/HandshakeTests.cs:5) pins round trips
  for boundary values, spans, and offsets, deterministic output, the exact
  wire size, the zero-allocation span round trip, and the empty-buffer rule.
- [EnvelopeTests.cs](../NsdRpc.Tests/EnvelopeTests.cs:6) pins the envelope
  wire facts: sizes, caps, kinds, boundary ids, trailing unknown bytes, and
  truncation safety.
- [RpcErrorTests.cs](../NsdRpc.Tests/RpcErrorTests.cs:6) pins the error round
  trip, the minimum wire size, deterministic output, and truncation safety.
- [TruncatedBufferTests.cs](../NsdRpc.Tests/TruncatedBufferTests.cs:6) pins
  the exact reader outcome at every truncation length and rejects a bad start
  and length.
- [RpcOptionsTests.cs](../NsdRpc.Tests/RpcOptionsTests.cs:5) pins the option
  defaults and the monotonic default clock.
- [RpcResultTests.cs](../NsdRpc.Tests/RpcResultTests.cs:6) pins the result
  values, the zero-code rejection, and zero allocation on success.

The engine tests pin the protocol rules in
[rpc-protocol](specs/rpc-protocol.md):

- [RpcPeerTests.cs](../NsdRpc.Tests/RpcPeerTests.cs:6) pins correlation,
  out-of-order responses, timeouts against a fake clock, cancellation, faults,
  streaming, keep-alive, the malformed frame and envelope rules, the
  drop-and-log rules, the dispatch fallbacks, and the argument guards.
- [PayloadDecodeTests.cs](../NsdRpc.Tests/PayloadDecodeTests.cs:6) pins the
  decode boundary: a reader failure maps to `InvalidParams` for a request,
  a drop and log for a notification, and a failed call for a chunk. The
  connection stays open.
- [RpcDispatcherTests.cs](../NsdRpc.Tests/RpcDispatcherTests.cs:6) pins
  dispatch errors, the app code range, directions, handler tokens, and silent
  notification failures.
- [RpcStreamingTests.cs](../NsdRpc.Tests/RpcStreamingTests.cs:7) pins the
  session wrapper, a waiting consumer, overflow, and every cancellation case.
- [RpcRegistryTests.cs](../NsdRpc.Tests/RpcRegistryTests.cs:22) pins fail-fast
  registration, scanning, explicit ids, and services.
- [RpcClientTests.cs](../NsdRpc.Tests/RpcClientTests.cs:6) and
  [RpcServerTests.cs](../NsdRpc.Tests/RpcServerTests.cs:6) pin the composition
  roots: calls, streams, notifications, accept, disconnect, prune, close, and
  the lifecycle guards.

The transport tests pin the TCP layer:

- [TcpFrameDecoderTests.cs](../NsdRpc.Tests/TcpFrameDecoderTests.cs:6) pins
  the framing contract, buffer growth, the cap, the idle shrink, and fuzzing.
- [TcpIntegrationTests.cs](../NsdRpc.Tests/TcpIntegrationTests.cs:10) pins
  the handshake and round trips over loopback sockets, the bad-handshake skip, the
  handshake framing errors, the send-queue bound, the disconnect paths, the
  per-read receive buffer rent and return, the idle connection holding no
  receive buffer, and the decoder idle shrink gate and no-thrash rule.

## Allocation coverage

Each allocation test warms up first, then measures
`GC.GetAllocatedBytesForCurrentThread`. Each test names the tolerated
allocations and the bound.

| Path | Test | Tolerance |
|---|---|---|
| Envelope span round trip, null payload | `EnvelopeSpanRoundTripAllocatesNothing` | Zero. |
| Envelope span round trip, payload | `EnvelopeSpanRoundTripWithAPayloadAllocatesOnlyThePayload` | The payload copy only. |
| Peer invoke setup | `InvokeSetupAllocatesThePendingCallAndThePayload` | The payload, the pending call, the completion source, the task, and the send drain state machine. |
| Chunk dispatch | `ChunkDispatchAllocatesNothingBeyondTheCodec` | The codec allocation, measured as a baseline. |
| TCP rejected send | `RejectedSendDoesNotAllocate` | Zero. |
| Registry lookup | `TryGetReadsAPublishedSnapshotWithoutAllocating` | Zero. |
| Dispatch with a token | `DispatchAllocatesOnlyTheCancellationSource` | The cancellation source. |
| Peer construction | `PeerConstructionDoesNotAllocateTheTimeoutScratch` | Below the scratch bound. |

Measurement limits:

- `GC.GetAllocatedBytesForCurrentThread` shows the calling thread only. Work
  that runs on the dispatch worker or the send drain does not appear.
- It does not show pooled buffer churn. A pooled rent allocates no managed
  memory.
- It counts the object header. A 128-byte array measures more than 128 bytes.
- A bound must leave room for the runtime. A tight bound flakes.

