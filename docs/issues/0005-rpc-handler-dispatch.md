# Improvement: Add the handler API, registry, and dispatcher

- Type: improvement
- Status: done
- Workflow: ../specs/issues.md

## Problem

Handlers are ordinary classes. The library registers them, scans for them,
and dispatches calls. No API existed. See
[../../specs/rpc-api.md](../../specs/rpc-api.md).

## Acceptance criteria

- [x] `RpcMethodAttribute` takes a `ulong` and a `RpcDirection`. The default direction is `Both`.
- [x] `IRpcHandler<TRequest, TResponse>.ExecuteAsync` takes the request, the context, and a `CancellationToken` last. A sync handler returns a completed `ValueTask` with zero allocation. A test proves the zero allocation. The parameters pass by value: the C# language forbids `in` parameters on async methods.
- [x] `RpcResult<T>` is a readonly struct with `Success` and `Failure` factories.
- [x] `RpcRegistry.Add` reads the id from the attribute. A missing attribute, a zero id, or a duplicate id fails fast.
- [x] `ScanAssembly` instantiates only marked classes that implement the handler interface with a parameterless constructor. A marked but non-conforming class fails fast.
- [x] `Freeze` locks the registry. A later `Add` throws.
- [x] `IPayloadSerializer<T>` exists with the spec members. The default wraps the generated nsd methods. A per-type cache holds one serializer per type.
- [x] `TryDeserialize` returns a result and never throws. A malformed payload answers `InvalidParams` at dispatch.
- [x] A direction mismatch answers `MethodNotFound`.
- [x] A handler exception answers `InternalError`. The wire carries no internal detail. The detail is logged locally.
- [x] A handler failure outside the app range is rejected.
- [x] One try/catch sits at the dispatch boundary. Dispatch is serial per peer.
- [x] The token passed to handlers fires on a received cancellation or a connection fault.
- [x] Every error path has a test.
- [x] The docs pass: [rpc-api](../../specs/rpc-api.md) gains `file:line` pointers, and [architecture](../../architecture.md) gains the module row.
