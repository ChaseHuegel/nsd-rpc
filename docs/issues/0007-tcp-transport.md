# Improvement: Add the NsdRpc.Tcp transport project

- Type: improvement
- Status: done
- Workflow: ../specs/issues.md

## Problem

The core library had no transport. TCP is the first one. It ships in a
sibling project so the core stays transport-agnostic and zero-dependency. See
[../../specs/rpc-api.md](../../specs/rpc-api.md).

## Acceptance criteria

- [x] `NsdRpc.Tcp` targets `netstandard2.1;net10.0`, references the core, has zero dependencies, and joins `NsdRpc.sln`.
- [x] The frame codec reads a 4-byte little-endian length prefix. It rejects a zero length, a length above `MaxFrameBytes`, and an incomplete frame. A fuzz test feeds random byte streams.
- [x] `TcpClientConnection` implements `IClientConnection`. It has `ConnectAsync`, a queue-based `TrySend`, a `Disconnected` event, and the `Handshake` exchange.
- [x] A handshake version mismatch closes the connection.
- [x] `TcpServerTransport` implements `IRpcServerTransport`. It has an accept loop, a counter starting at 1 for `ClientId`, a handshake per accept, and `Close`.
- [x] The RPC engine never sees the handshake. The first engine frame after handshake is an envelope.
- [x] Socket buffers, timeouts, and limits are config keys, not magic constants.
- [x] An abrupt disconnect fires `Disconnected` and faults pending calls with `Unavailable`.
- [x] Tests live in `NsdRpc.Tests`, which gains a project reference to `NsdRpc.Tcp`. The CI workflows build the new project.
- [x] The docs pass: [rpc-api](../../specs/rpc-api.md) gains pointers, and [architecture](../../architecture.md) gains the project row.

## Notes

The receive path surfaced a Layer 0 gap: the interfaces had no inbound frame
channel. `IRpcConnection` gains a `FrameReceived` event. The peer subscribes
at construction. Pull-based transports never fire it and keep using
`HandleFrame`. The framing boundary also moved: the peer builds the length
prefix on sends and validates it on receives, so the frame contract holds on
every transport.
