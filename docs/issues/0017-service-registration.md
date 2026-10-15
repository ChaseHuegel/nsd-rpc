# Improvement: Service registration and stream declaration

- Type: improvement
- Status: done
- Workflow: ../specs/issues.md

## Problem

A generated handler base implements one `IRpcHandler` per method and must
register each with its method id, but `RpcRegistry.Add` read a single id
from a class-level attribute. A generated service needed an id-explicit
registration path. A server contract also needed to declare its stream
chunk type, which `RpcMethodAttribute` could not express.

## Acceptance criteria

- [x] `RpcRegistry.Add<TRequest, TResponse>(handler, ulong methodId, RpcDirection direction = Both)` registers with an explicit id and fails fast on zero or duplicate ids.
- [x] `IRpcServiceRegistrar` exposes `void RegisterHandlers(RpcRegistry registry)`.
- [x] `RpcRegistry.AddService(IRpcServiceRegistrar service)` delegates to `RegisterHandlers`.
- [x] `RpcMethodAttribute` gains a named `Type? Streams` property.
- [x] Tests cover every overload and the fail-fast paths.
- [x] The docs pass updates [rpc-api](../../specs/rpc-api.md).
