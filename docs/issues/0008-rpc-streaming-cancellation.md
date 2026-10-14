# Improvement: Prove streaming and cancellation end to end

- Type: improvement
- Status: open
- Workflow: ../specs/issues.md

## Problem

Streams and cancellation are the two extensions beyond JSON-RPC. The wire
rules exist in [../../specs/rpc-protocol.md](../../specs/rpc-protocol.md). No
end-to-end path exists. Issues 0004 and 0005 add the pieces. This issue wires
them and proves every rule.

## Acceptance criteria

- [ ] `IChunkSink<TChunk>` exists. The callback path delivers chunks in order, resets the deadline per chunk, and completes with the response.
- [ ] `RpcStreamSession<TChunk, TResponse>` gives `Chunks` as `IAsyncEnumerable` and `Response` as a `Task`. The queue is bounded by `MaxBufferedChunks`. A full queue faults the stream with `Busy`.
- [ ] A chunk after completion is dropped and logged.
- [ ] A handler streams with `context.Stream` and then calls `Respond` exactly once. Streaming after `Respond`, or `Respond` twice, asserts.
- [ ] All four cancellation cases pass: before dispatch, during the handler, after the response is in flight, and a late response at the client.
- [ ] A deadline sends a cancellation. A test records frames with the fake clock and the fake transport.
- [ ] Chunks after a cancellation are dropped on both sides.
- [ ] A full scenario test streams several chunks, then one response, through the in-memory pair.
- [ ] The chunk callback path allocates nothing per chunk beyond the codec. A test measures with `GC.GetAllocatedBytesForCurrentThread`.
- [ ] The docs pass: [rpc-api](../../specs/rpc-api.md) gains pointers, and [testing](../../testing.md) lists the new coverage.
