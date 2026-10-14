# Improvement: Notification shape in the RpcService generator

- Type: improvement
- Status: done
- Workflow: ../specs/issues.md

## Problem

The generator supported plain calls and streams but not notifications.
`Notify<TRequest>` had no generated equivalent, so the generated chat
sample sent chat through the raw API. A `void` method shape closes that
gap.

## Acceptance criteria

- [x] A `[RpcService]` method returning `void` generates a forwarding call to `Notify<TRequest>`, or to the no-payload `Notify(method)` when it has no parameters.
- [x] Zero or one request parameter is valid. A token, a timeout, a sink, or a second parameter is RSG1004. A non-struct request is RSG1005.
- [x] The method's name and id flow from the `[RpcMethod]` attribute as today.
- [x] The generator emits a `{MethodName}Handler` base implementing `IRpcHandler<TRequest, RpcUnit>`. The base answers `MethodNotFound` to a request on the notification id, so the `RpcUnit` placeholder is never serialized.
- [x] The generator emits one `RpcUnit` struct per compilation.
- [x] Tests cover the generated text, the diagnostics, and a compile-and-run notification send plus handler registration.
- [x] The generated chat sample adopts the shape for chat. The docs pass updates the "Typed service clients" section in [rpc-api](../../specs/rpc-api.md).
