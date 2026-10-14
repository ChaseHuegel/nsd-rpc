# Improvement: Server-side handler skeletons from RpcService

- Type: improvement
- Status: open
- Workflow: ../specs/issues.md

## Problem

The `[RpcService]` interface generates only the client half. The handler
half still repeats the boilerplate: implement `IRpcHandler<TRequest,
TResponse>`, thread the `RpcContext`, and repeat the `[RpcMethod]` on a
class. The same declaration can produce a handler base with the same
no-boilerplate story.

## Acceptance criteria

- [ ] The generator also emits a `{Name}Handler` abstract base for each `[RpcService]` interface.
- [ ] The base implements `IRpcHandler<TRequest, TResponse>` and carries `[RpcMethod]`, so `RpcRegistry.Add` sees it through inheritance.
- [ ] Each service method becomes a protected typed method taking the request, the `RpcContext`, and a `CancellationToken`.
- [ ] Streaming works server-side: the handler can call `context.Stream` before returning the result.
- [ ] Tests cover a concrete subclass driven end to end over the in-memory pair.
- [ ] The docs pass updates the "Typed service clients" section in [rpc-api](../../specs/rpc-api.md).
