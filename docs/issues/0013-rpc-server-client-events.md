# Improvement: Public client connection events on RpcServer

- Type: improvement
- Status: done
- Workflow: ../specs/issues.md

## Problem

A server could not observe its connected clients. `RpcServer` exposed only
`GetPeer(clientId)`. Broadcasting or tracking clients needed a public
signal. The sample chat app needs to rebroadcast a notification to every
client, so this was a prerequisite.

## Acceptance criteria

- [x] `RpcServer` exposes `event Action<RpcPeer> ClientConnected`.
- [x] `RpcServer` exposes `event Action<RpcPeer> ClientDisconnected`.
- [x] `ClientConnected` fires after the accept loop wires a peer. The internal `PeerAccepted` hook left.
- [x] `ClientDisconnected` fires on the disconnect event, on the prune sweep, and on `Close`.
- [x] `RpcPeer.ClientId` is public. A sample keys its peer table by it.
- [x] Tests cover every fire path.
- [x] The docs pass updates the `RpcServer` block in [rpc-api](../../specs/rpc-api.md).
