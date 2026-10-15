# Improvement: Shape-based client and handler generation

- Type: improvement
- Status: open
- Workflow: ../specs/issues.md

## Problem

`[RpcService]` on an interface generates a client plus one handler base per
method. The role should come from the method signature instead: an
invocation shape generates a client member, a handler shape generates one
member on a single abstract base. That removes the per-method bases and the
deployment-role ambiguity.

## Acceptance criteria

- [ ] A method without `RpcContext` is invocation-shaped. A method with `RpcContext` is handler-shaped.
- [ ] Invocation-shaped methods generate a `{Name}Client` class, unchanged from today.
- [ ] Handler-shaped methods generate one `{Name}Handler` abstract base implementing the interface, `IRpcServiceRegistrar`, and one `IRpcHandler<,>` per method. Notifications use the guarded `RpcUnit`.
- [ ] `RegisterHandlers` calls the id-explicit `Add` per handler with its direction.
- [ ] A `void` handler method becomes an abstract `void` receive method. A `void` invocation method stays the client notify.
- [ ] A duplex method is an invocation and a handler sharing one id; the generator cross-checks matching request types and, for streams, the chunk type against `Streams`.
- [ ] Diagnostics: shape mixing, duplicate request types on the handler base, a duplicate id within the same role, and a stream chunk type mismatch.
- [ ] The per-method `{Method}Handler` bases are removed.
- [ ] Tests are rewritten around the two shapes and the duplex pair.
