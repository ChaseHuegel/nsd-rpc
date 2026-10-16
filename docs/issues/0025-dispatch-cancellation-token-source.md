# Improvement: Avoid the linked cancellation source per dispatch

- Type: improvement
- Status: open
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

- [ ] One plain `CancellationTokenSource` covers one inbound request. No linked source and no registration on the connection token exist.
- [ ] A cancellation frame cancels the token of a running handler.
- [ ] A disconnect cancels the tokens of all running handlers.
- [ ] A test measures the dispatch allocations for one request and asserts the recorded bound.
- [ ] Every cancellation test passes unchanged.
- [ ] The docs pass updates [rpc-api](../specs/rpc-api.md) if the token lifetime description changes.
