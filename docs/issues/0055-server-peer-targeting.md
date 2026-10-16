# Improvement: Server targets peers, not a broadcast

- Type: improvement
- Status: done
- Workflow: ../specs/issues.md

## Problem

`RpcServer` implements `IRpcHost : IRpcCaller`, so its inherited call surface
broadcasts to every peer. `InvokeAsync` calls every peer and returns one
response, "the first failure wins, else the last success"
([RpcServer.cs:66](../../NsdRpc/RpcServer.cs:66)). `InvokeStreamAsync` fans
every peer's chunks into one sink or session
([RpcServer.cs:87](../../NsdRpc/RpcServer.cs:87)). `Notify` sends to every
peer and returns one aggregate bool ([RpcServer.cs:136](../../NsdRpc/RpcServer.cs:136)).
No peer set silently fails with `Unavailable`.

The broadcast is implicit. A server call looks like a normal call, the
response is one arbitrary peer's response, stream chunks lose peer
attribution, and the caller cannot choose the recipients. A handler has no
path to the peers at all, so the chat samples inject a peer dictionary built
from the connect and disconnect events.

## Acceptance criteria

- [x] `RpcServer` is not an `IRpcCaller`. It exposes `Peers` and `GetPeer`
      and no broadcast surface.
- [x] `IRpcHost` keeps `Peers` and does not extend `IRpcCaller`.
- [x] `Peers` returns a `PeerSet`, an immutable snapshot that iterates without
      allocating and does not change when the live peers change.
- [x] `RpcContext.Host` exposes the endpoint that received the message, so a
      handler fans out through `context.Host.Peers`.
- [x] `RpcPeer` and `RpcClient` declare `IRpcCaller` explicitly.
- [x] Tests cover an empty server, handler fan-out through the context, peer
      targeting, snapshot immutability, and allocation-free iteration.
- [x] The docs pass updates [rpc-api](../../specs/rpc-api.md) and
      [quick-reference](../../quick-reference.md), and updates both chat
      samples to fan out through the context.

## Resolution

`RpcServer` is `IRpcHost` only. `IRpcHost.Peers` returns a `PeerSet` over a
copy-on-write array. `RpcContext.Host` exposes the endpoint, so a handler
fans out through `context.Host.Peers` and holds no peer set. The broadcast
surface and its aggregation are removed.
