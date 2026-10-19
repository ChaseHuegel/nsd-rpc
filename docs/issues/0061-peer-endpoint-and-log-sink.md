# Improvement: Peer endpoint, peer close, and log sink

- Type: improvement
- Status: done
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

- [x] `RpcPeer.RemoteEndPoint` returns the remote endpoint on the server
      side. It is null on the client side.
- [x] `RpcPeer.Close()` closes the peer's connection. The `Disconnected`
      event fires once with `LocalClose`.
- [x] `RpcPeer.Connection` stays internal.
- [x] `RpcOptions.LogSink` accepts a delegate with a level, a message, and
      an optional exception. Null keeps the TraceSource default. `RpcLog`
      stays internal.
- [x] No `Microsoft.Extensions.Logging` dependency.
- [x] A test proves `RemoteEndPoint` is available on the server and null on
      the client.
- [x] A test proves `RpcPeer.Close` fires `LocalClose` once.
- [x] A test proves a custom sink receives drop and error events.
- [x] The docs pass updates `rpc-api` and `quick-reference`.
- [x] No wire change.

## Implementation notes

- Keep the API minimal. Do not add a middleware pipeline.
- A sink that throws must not kill the dispatch thread. Catch and drop the
  sink failure.
- `LogSink` reads at construction, like the other `RpcOptions` keys.

## Resolution

`RpcPeer.RemoteEndPoint` reads the client endpoint from a server connection
and returns null on the client side. `RpcPeer.Close` closes the connection
with `LocalClose`. The raw connection stays internal. `RpcOptions.LogSink`
accepts the level, the message, and an optional exception, and a null sink
keeps the TraceSource default. The sink is not gated by the TraceSource
switch, and a throwing sink is contained. `RpcLog` stays internal. The
library keeps zero runtime dependencies.
