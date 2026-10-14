# Improvement: Notification shape in the RpcService generator

- Type: improvement
- Status: open
- Workflow: ../specs/issues.md

## Problem

The generator supports plain calls and streams but not notifications.
`Notify<TRequest>` has no generated equivalent, so the generated chat
sample sends chat through the raw API. A notification shape would close
that gap.

## Acceptance criteria

- [ ] A `[RpcService]` method returning `bool` with a single request parameter generates a forwarding call to `Notify<TRequest>`.
- [ ] The method's name and id flow from the `[RpcMethod]` attribute as today.
- [ ] Diagnostics cover a `bool` method with a wrong parameter shape.
- [ ] Tests cover the generated text and a compile-and-run notification send.
- [ ] The docs pass updates the "Typed service clients" section in [rpc-api](../../specs/rpc-api.md).
