# Improvement: Server-side handler skeletons from RpcService

- Type: improvement
- Status: done
- Workflow: ../specs/issues.md

## Problem

The `[RpcService]` interface generated only the client half. The handler
half still repeated the boilerplate: implement `IRpcHandler<TRequest,
TResponse>`, thread the `RpcContext`, and repeat the `[RpcMethod]` on a
class. The same declaration produces a handler base with the same
no-boilerplate story.

## Acceptance criteria

- [x] The generator emits one `{MethodName}Handler` abstract base per service method. A session and a sink overload share one base because they share the method id.
- [x] The base implements `IRpcHandler<TRequest, TResponse>` and carries `[RpcMethod]` with the interface method's direction, so `RpcRegistry.Add` sees it through inheritance.
- [x] The method becomes a protected abstract typed method taking the request, the `RpcContext`, and a `CancellationToken`.
- [x] `RpcRegistry.Add` and `ScanAssembly` read `[RpcMethod]` with inheritance, so a concrete subclass registers without repeating the id.
- [x] Streaming works server-side: the `RpcContext` passes through, so the handler streams with `context.Stream`. The runtime stream path is pinned by the engine tests.
- [x] Tests cover the generated base text, a compile-and-run subclass registration, and an inheritance test in the core suite.
- [x] The docs pass updates the "Typed service clients" section in [rpc-api](../../specs/rpc-api.md).
