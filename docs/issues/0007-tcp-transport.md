# Improvement: Add the NsdRpc.Tcp transport project

- Type: improvement
- Status: open
- Workflow: ../specs/issues.md

## Problem

The core library has no transport. TCP is the first one. It ships in a
sibling project so the core stays transport-agnostic and zero-dependency. See
[../../specs/rpc-api.md](../../specs/rpc-api.md).

## Acceptance criteria

- [ ] `NsdRpc.Tcp` targets `netstandard2.1;net10.0`, references the core, has zero dependencies, and joins `NsdRpc.sln`.
- [ ] The frame codec reads a 4-byte little-endian length prefix. It rejects a zero length, a length above `MaxFrameBytes`, and an incomplete frame. A fuzz test feeds random byte streams.
- [ ] `TcpClientConnection` implements `IClientConnection`. It has `ConnectAsync`, a queue-based `TrySend`, a `Disconnected` event, and the `Handshake` exchange.
- [ ] A handshake version mismatch closes the connection.
- [ ] `TcpServerTransport` implements `IRpcServerTransport`. It has an accept loop, a counter starting at 1 for `ClientId`, a handshake per accept, and `Close`.
- [ ] The RPC engine never sees the handshake. A test proves the first engine frame after handshake is an envelope.
- [ ] Socket buffers, timeouts, and limits are config keys, not magic constants.
- [ ] An abrupt disconnect fires `Disconnected` and faults pending calls with `Unavailable`.
- [ ] Tests live in `NsdRpc.Tests`, which gains a project reference to `NsdRpc.Tcp`. The CI workflows build the new project.
- [ ] The docs pass: [rpc-api](../../specs/rpc-api.md) gains pointers, and [architecture](../../architecture.md) gains the project row.
