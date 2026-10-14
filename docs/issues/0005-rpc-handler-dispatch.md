# Improvement: Add the handler API, registry, and dispatcher

- Type: improvement
- Status: open
- Workflow: ../specs/issues.md

## Problem

Handlers are ordinary classes. The library registers them, scans for them,
and dispatches calls. No API exists. See
[../../specs/rpc-api.md](../../specs/rpc-api.md).

## Acceptance criteria

- [ ] `RpcMethodAttribute` takes a `ulong` and a `RpcDirection`. The default direction is `Both`.
- [ ] `IRpcHandler<TRequest, TResponse>.ExecuteAsync` takes `in TRequest`, `in RpcContext`, and a `CancellationToken` last. A sync handler returns a completed `ValueTask` with zero allocation. A test proves the zero allocation.
- [ ] `RpcResult<T>` is a readonly struct with `Success` and `Failure` factories.
- [ ] `RpcRegistry.Add` reads the id from the attribute. A missing attribute, a zero id, or a duplicate id fails fast.
- [ ] `ScanAssembly` instantiates only marked classes that implement the handler interface with a parameterless constructor. A marked but non-conforming class fails fast.
- [ ] `Freeze` locks the registry. A later `Add` throws.
- [ ] `IPayloadSerializer<T>` exists with the spec members. The default wraps the generated nsd methods. A per-type cache holds one serializer per type.
- [ ] `TryDeserialize` returns a result and never throws. A malformed payload answers `InvalidParams` at dispatch.
- [ ] A direction mismatch answers `MethodNotFound`.
- [ ] A handler exception answers `InternalError`. The wire carries no internal detail. The detail is logged locally.
- [ ] A handler failure outside the app range is rejected.
- [ ] One try/catch sits at the dispatch boundary. Dispatch is serial per peer.
- [ ] The token passed to handlers fires on a received cancellation or a connection fault.
- [ ] Every error path has a test.
- [ ] The docs pass: [rpc-api](../../specs/rpc-api.md) gains `file:line` pointers, and [architecture](../../architecture.md) gains the module row.
