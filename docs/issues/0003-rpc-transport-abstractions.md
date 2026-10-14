# Improvement: Add Layer 0 transport abstractions and options

- Type: improvement
- Status: open
- Workflow: ../specs/issues.md

## Problem

The API spec defines the transport layer. No types exist. The peer (issue
0004) and the TCP project (issue 0007) build on them. See
[../../specs/rpc-api.md](../../specs/rpc-api.md).

## Acceptance criteria

- [ ] `IRpcConnection`, `IClientConnection`, `IServerConnection`, and `IRpcServerTransport` match the spec exactly in members and contracts.
- [ ] `RpcOptions` has the ratified defaults: `Timeout` 30000, `MaxFrameBytes` 65536, `MaxPendingCalls` 1024, `MaxBufferedChunks` 1024, and a monotonic `TimestampNanos`.
- [ ] A null options argument uses a private default instance. No magic constants.
- [ ] The frame contract is documented on `IRpcConnection`: a 4-byte little-endian length prefix and the envelope bytes. A zero length or a length above `MaxFrameBytes` is a protocol error.
- [ ] `TrySend` copies the frame before it returns and returns false when closed. Tests use a fake connection that records frames.
- [ ] `Disconnected` fires once. A test proves a second disconnect does not refire it.
- [ ] No transport implementation ships in this issue. The TCP project is issue 0007.
- [ ] The docs pass: [rpc-api](../../specs/rpc-api.md) gains `file:line` pointers, and [architecture](../../architecture.md) gains the module row.
