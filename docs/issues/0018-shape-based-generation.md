# Improvement: Shape-based client and handler generation

- Type: improvement
- Status: done
- Workflow: ../specs/issues.md

## Problem

`[RpcService]` on an interface generated a client plus one handler base per
method. The role now comes from the method signature instead: an invocation
shape generates a client member, a handler shape generates one member on a
single abstract base. That removes the per-method bases and the
deployment-role ambiguity.

## Acceptance criteria

- [x] A method without `RpcContext` is invocation-shaped. A method with `RpcContext` is handler-shaped.
- [x] Invocation-shaped methods generate a `{Name}Client` class, unchanged from today.
- [x] Handler-shaped methods generate one `{Name}Handler` abstract base implementing the interface, `IRpcServiceRegistrar`, and one `IRpcHandler<,>` per method. Notifications use the guarded `RpcUnit`.
- [x] `RegisterHandlers` calls the id-explicit `Add` per handler with its direction.
- [x] A `void` handler method becomes an abstract `void` receive method. A `void` invocation method stays the client notify.
- [x] A duplex method is an invocation and a handler sharing one id; the generator cross-checks matching request types and, for streams, the chunk type against `Streams`.
- [x] Diagnostics: shape mixing, duplicate request types on the handler base, a duplicate id within the same role, and a stream chunk type mismatch. Shape mixing surfaces as RSG1006; the others are RSG1010 and RSG1011.
- [x] The per-method `{MethodName}Handler` bases are removed.
- [x] Tests are rewritten around the two shapes and the duplex pair.

## Notes

The handler base is `abstract` but its handler members are `virtual` with
defaults: a partial implementer overrides only what it handles. An unhandled
call answers `InternalError`, an unhandled notification is a no-op. This
came from the sample: a client handling only the chat notification still
subclasses the same base and overrides just that one member.
