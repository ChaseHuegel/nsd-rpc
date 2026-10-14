# Improvement: Public client connection events on RpcServer

- Type: improvement
- Status: open
- Workflow: ../specs/issues.md

## Problem

A server cannot observe its connected clients. `RpcServer` exposes only
`GetPeer(clientId)`. Broadcasting or tracking clients needs a public
signal. The sample chat app needs to rebroadcast a notification to every
client, so this is a prerequisite.

## Acceptance criteria

- [ ] `RpcServer` exposes `event Action<RpcPeer> ClientConnected`.
- [ ] `RpcServer` exposes `event Action<RpcPeer> ClientDisconnected`.
- [ ] `ClientConnected` fires after the accept loop wires a peer. The internal `PeerAccepted` hook leaves.
- [ ] `ClientDisconnected` fires on the disconnect event, on the prune sweep, and on `Close`.
- [ ] `RpcPeer.ClientId` is public. A sample keys its peer table by it.
- [ ] Tests cover every fire path.
- [ ] The docs pass updates the `RpcServer` block in [rpc-api](../../specs/rpc-api.md).
