# Improvement: IRpcHost and the server broadcast call surface

- Type: improvement
- Status: open
- Workflow: ../specs/issues.md

## Problem

A server reaches many peers but has no call surface of its own. `IRpcHost`
would unify `RpcClient`, `RpcPeer`, and `RpcServer` behind one interface:
the call surface delivers to every peer the endpoint reaches, with a `Peers`
collection for targeting a subset.

## Acceptance criteria

- [ ] `IRpcHost : IRpcCaller` exposes `IReadOnlyCollection<RpcPeer> Peers`.
- [ ] `RpcPeer` and `RpcClient` implement it with a single-element collection.
- [ ] `RpcServer` implements it with a peer snapshot and broadcast semantics: `Notify` to all, `InvokeAsync` with first-failure-else-last-success, and stream fan-in through both overloads.
- [ ] Empty peers completes calls with `Unavailable`. The token and timeout apply per peer.
- [ ] Tests cover every aggregation rule.
- [ ] The docs pass updates [rpc-api](../../specs/rpc-api.md).
