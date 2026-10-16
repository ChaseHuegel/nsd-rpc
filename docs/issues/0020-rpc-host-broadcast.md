# Improvement: IRpcHost and the server broadcast call surface

- Type: improvement
- Status: done
- Workflow: ../specs/issues.md

## Problem

A server reaches many peers but has no call surface of its own. `IRpcHost`
would unify `RpcClient`, `RpcPeer`, and `RpcServer` behind one interface:
the call surface delivers to every peer the endpoint reaches, with a `Peers`
collection for targeting a subset.

## Acceptance criteria

- [x] `IRpcHost : IRpcCaller` exposes `IReadOnlyCollection<RpcPeer> Peers`.
- [x] `RpcPeer` and `RpcClient` implement it with a single-element collection.
- [x] `RpcServer` implements it with a peer snapshot and broadcast semantics: `Notify` to all, `InvokeAsync` with first-failure-else-last-success, and stream fan-in through both overloads.
- [x] Empty peers completes calls with `Unavailable`. The token and timeout apply per peer.
- [x] Tests cover every aggregation rule.
- [x] The docs pass updates [rpc-api](../../specs/rpc-api.md).

## Resolution

`IRpcHost` adds `Peers`. `RpcPeer` and `RpcClient` expose one peer.
`RpcServer` snapshots its peers and broadcasts. The aggregation rules and the
no-peer behavior are in [rpc-api](../../specs/rpc-api.md).
