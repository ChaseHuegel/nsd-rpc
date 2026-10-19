# Improvement: Peer endpoint, peer close, and log sink

- Type: improvement
- Status: open
- Workflow: ../specs/issues.md

## Problem

A host cannot finish its operational surface on the public API.

- `RpcPeer.Connection` is internal
  ([RpcPeer.cs:62](../../NsdRpc/Engine/RpcPeer.cs:62)). A host cannot read
  the remote endpoint of a server peer. A host cannot disconnect one peer.
- `RpcLog` is internal and has no consumer hook
  ([RpcLog.cs](../../NsdRpc/Engine/RpcLog.cs)). A host cannot route drop and
  error events to its logger.

## Acceptance criteria

- [ ] `RpcPeer.RemoteEndPoint` returns the remote endpoint on the server
      side. It is null on the client side.
- [ ] `RpcPeer.Close()` closes the peer's connection. The `Disconnected`
      event fires once with `LocalClose`.
- [ ] `RpcPeer.Connection` stays internal.
- [ ] `RpcOptions.LogSink` accepts a delegate with a level, a message, and
      an optional exception. Null keeps the TraceSource default. `RpcLog`
      stays internal.
- [ ] No `Microsoft.Extensions.Logging` dependency.
- [ ] A test proves `RemoteEndPoint` is available on the server and null on
      the client.
- [ ] A test proves `RpcPeer.Close` fires `LocalClose` once.
- [ ] A test proves a custom sink receives drop and error events.
- [ ] The docs pass updates `rpc-api` and `quick-reference`.
- [ ] No wire change.

## Implementation notes

- Keep the API minimal. Do not add a middleware pipeline.
- A sink that throws must not kill the dispatch thread. Catch and drop the
  sink failure.
- `LogSink` reads at construction, like the other `RpcOptions` keys.
