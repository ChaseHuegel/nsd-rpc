# Improvement: Add Layer 0 transport abstractions and options

- Type: improvement
- Status: done
- Workflow: ../specs/issues.md

## Problem

The API spec defined the transport layer. No types existed. The peer (issue
0004) and the TCP project (issue 0007) build on them. See
[../../specs/rpc-api.md](../../specs/rpc-api.md).

## Acceptance criteria

- [x] `IRpcConnection`, `IClientConnection`, `IServerConnection`, and `IRpcServerTransport` match the spec exactly in members and contracts.
- [x] `RpcOptions` has the ratified defaults: `Timeout` 30000, `MaxFrameBytes` 65536, `MaxPendingCalls` 1024, `MaxBufferedChunks` 1024, and a monotonic `TimestampNanos`.
- [x] A null options argument uses a private default instance. No magic constants. The peer wiring lands in issue 0004.
- [x] The frame contract is documented on `IRpcConnection`: a 4-byte little-endian length prefix and the envelope bytes. A zero length or a length above `MaxFrameBytes` is a protocol error.
- [x] `TrySend` copies the frame before it returns and returns false when closed. Tests use a fake connection that records frames.
- [x] `Disconnected` fires once. A test proves a second disconnect does not refire it.
- [x] No transport implementation ships in this issue. The TCP project is issue 0007.
- [x] The docs pass: [rpc-api](../../specs/rpc-api.md) gains `file:line` pointers, and [architecture](../../architecture.md) gains the module rows.
