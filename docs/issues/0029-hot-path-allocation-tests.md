# Improvement: Prove hot-path allocations with real payloads

- Type: improvement
- Status: open
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

- [ ] Allocation tests cover the envelope round trip with a non-null payload, the peer invoke path, the dispatch path, the chunk path, and the TCP `TrySend` path.
- [ ] Each test warms up first. Each test names its expected allocation count and the reason for every tolerated allocation.
- [ ] The tests use no sleeps and no timing.
- [ ] [testing.md](../testing.md) records the covered paths and the measurement limits.
- [ ] The suite fails when a covered path adds an allocation.
