# Improvement: Avoid the linked cancellation source per dispatch

- Type: improvement
- Status: done
- Workflow: ../specs/issues.md

## Problem

`RpcDispatcher.DispatchAsync` creates a linked cancellation source for every
inbound request with a nonzero id:

```csharp
cancelSource = CancellationTokenSource.CreateLinkedTokenSource(_peer.ConnectionToken);
```

See [NsdRpc/Handlers/RpcDispatcher.cs:34](../../NsdRpc/Handlers/RpcDispatcher.cs:34).
The call allocates a `CancellationTokenSource` and a registration on the
connection token. A handler that ignores the token still pays the cost. This
is one allocation graph per request on the dispatch hot path.

A plain source per request works instead. The peer already tracks every
inbound call. On a fault the peer cancels the attached sources before it
clears the inbound set. See
[NsdRpc/Engine/RpcPeer.cs:387](../../NsdRpc/Engine/RpcPeer.cs:387).

## Acceptance criteria

- [x] One plain `CancellationTokenSource` covers one inbound request. No linked source and no registration on the connection token exist.
- [x] A cancellation frame cancels the token of a running handler.
- [x] A disconnect cancels the tokens of all running handlers.
- [x] A test measures the dispatch allocations for one request and asserts the recorded bound.
- [x] Every cancellation test passes unchanged.
- [x] The docs pass updates [rpc-api](../specs/rpc-api.md) if the token lifetime description changes.

## Resolution

The dispatcher creates one plain `CancellationTokenSource` per inbound
request. No linked source and no registration on a connection token exist.
`FaultPending` cancels every attached source before it clears the inbound set,
so a disconnect still cancels running handlers.

The allocation test records 320 bytes per dispatch and asserts a bound of
384. The handlers and the cancellation behavior do not change, so
[rpc-api](../specs/rpc-api.md) needs no update.
