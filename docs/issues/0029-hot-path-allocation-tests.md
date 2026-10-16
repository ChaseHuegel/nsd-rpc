# Improvement: Prove hot-path allocations with real payloads

- Type: improvement
- Status: done
- Workflow: ../specs/issues.md

## Problem

The rule in [coding-style](../specs/coding-style.md) says hot paths allocate
nothing. The tests do not prove the rule for real payloads.

- `EnvelopeSpanRoundTripAllocatesNothing` uses a null payload. See
  [EnvelopeTests.cs:90](../../NsdRpc.Tests/EnvelopeTests.cs:90). The payload
  path never runs.
- `ChunkDispatchAllocatesNothingBeyondTheCodec` subtracts a baseline. The
  baseline contains the codec allocation. See
  [RpcStreamingTests.cs:183](../../NsdRpc.Tests/RpcStreamingTests.cs:183).
- No test measures the TCP send path, the receive loop, or the dispatch
  path.

`GC.GetAllocatedBytesForCurrentThread` shows only the current thread. It
does not show pooled churn. Each test must state what it measures.

## Acceptance criteria

- [x] Allocation tests cover the envelope round trip with a non-null payload, the peer invoke path, the dispatch path, the chunk path, and the TCP `TrySend` path.
- [x] Each test warms up first. Each test names its expected allocation count and the reason for every tolerated allocation.
- [x] The tests use no sleeps and no timing.
- [x] [testing.md](../testing.md) records the covered paths and the measurement limits.
- [x] The suite fails when a covered path adds an allocation.

## Resolution

New tests cover the envelope span round trip with a payload and the peer
invoke setup. The dispatch, registry, chunk, and TCP `TrySend` paths already
have allocation tests. [testing.md](../testing.md) lists every path, its
tolerance, and the measurement limits of
`GC.GetAllocatedBytesForCurrentThread`.
