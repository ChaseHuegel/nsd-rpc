# Improvement: Service registration and stream declaration

- Type: improvement
- Status: open
- Workflow: ../specs/issues.md

## Problem

A generated handler base implements one `IRpcHandler` per method and must
register each with its method id, but `RpcRegistry.Add` reads a single id
from a class-level attribute. A generated service needs an id-explicit
registration path. A server contract also needs to declare its stream
chunk type, which `RpcMethodAttribute` cannot express.

## Acceptance criteria

- [ ] `RpcRegistry.Add<TRequest, TResponse>(handler, ulong methodId, RpcDirection direction = Both)` registers with an explicit id and fails fast on zero or duplicate ids.
- [ ] `IRpcServiceRegistrar` exposes `void RegisterHandlers(RpcRegistry registry)`.
- [ ] `RpcRegistry.AddService(IRpcServiceRegistrar service)` delegates to `RegisterHandlers`.
- [ ] `RpcMethodAttribute` gains a named `Type? Streams` property.
- [ ] Tests cover every overload and the fail-fast paths.
- [ ] The docs pass updates [rpc-api](../../specs/rpc-api.md).
