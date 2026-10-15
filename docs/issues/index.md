# Issue Index

The live tracker for this repository. Work Active Sprint items in order.
Read the format and workflow in [specs/issues.md](specs/issues.md) before you
add or work an issue.

## Active Sprint

### Bug

- [x] [Deserialize reads out of bounds on truncated buffers](/docs/issues/0001-malformed-buffer-deserialize-overread.md) — generated deserializers over-read malformed input.

### Improvement

- [x] [Add the RPC envelope schema and pin the wire contract](/docs/issues/0002-rpc-envelope-schema.md) — the schema, the kinds, and the error codes, with wire tests.
- [x] [Add Layer 0 transport abstractions and options](/docs/issues/0003-rpc-transport-abstractions.md) — the connection interfaces and `RpcOptions`.
- [x] [Add RpcPeer, the protocol engine](/docs/issues/0004-rpc-peer-engine.md) — correlation, timeouts, cancellation, and faults.
- [x] [Add the handler API, registry, and dispatcher](/docs/issues/0005-rpc-handler-dispatch.md) — attributes, scanning, results, and payload codecs.
- [x] [Add RpcClient and RpcServer composition](/docs/issues/0006-rpc-client-server.md) — the two composition roots and their lifecycle.
- [x] [Add the NsdRpc.Tcp transport project](/docs/issues/0007-tcp-transport.md) — framing, sockets, and the handshake.
- [x] [Prove streaming and cancellation end to end](/docs/issues/0008-rpc-streaming-cancellation.md) — chunks, keep-alive, and every cancellation case.
- [x] [nsdc generated output lacks the nullable directive](/docs/issues/0009-nsdc-nullable-directive.md) — nsdc 1.3.4 emits `#nullable enable`; the pipeline workaround is gone.
- [x] [Typed service clients via the RpcService source generator](/docs/issues/0010-rpc-service-generator.md) — a generator emits `{Name}Client` from `[RpcService]` interfaces.
- [x] [Server-side handler skeletons from RpcService](/docs/issues/0011-rpc-service-handler-skeletons.md) — the generator also emits `{MethodName}Handler` bases.
- [x] [Peer-based constructor for generated service clients](/docs/issues/0012-rpc-service-peer-constructor.md) — a `(RpcPeer)` constructor variant for server-side outbound.
- [x] [Public client connection events on RpcServer](/docs/issues/0013-rpc-server-client-events.md) — `ClientConnected` and `ClientDisconnected` for broadcasting and tracking.
- [x] [Add the raw chat sample](/docs/issues/0014-raw-chat-sample.md) — the shared schema plus the bare-API console app.
- [x] [Add the generated chat sample](/docs/issues/0015-generated-chat-sample.md) — the same pattern through the source generator.
- [x] [Service registration and stream declaration](/docs/issues/0017-service-registration.md) — id-explicit `Add`, `AddService`, and `RpcMethodAttribute.Streams`.
- [x] [Shape-based client and handler generation](/docs/issues/0018-shape-based-generation.md) — invocation shapes make the client, handler shapes make one `{Name}Handler` base.
- [x] [Rewrite the generated chat sample on one duplex interface](/docs/issues/0019-duplex-chat-sample.md) — the sample adopts the single-interface duplex shape.
- [x] [Migrate schema generation to the needlefish source generator](/docs/issues/0021-migrate-to-needlefish-generator.md) — drop the nsdc build step for `Needlefish.Generators`.

### Research

<!-- - [ ] [Title](/docs/issues/0000-slug.md) — one-line summary -->

## Backlog

### Bug

<!-- - [ ] [Title](/docs/issues/0000-slug.md) — one-line summary -->

### Improvement

- [x] [Notification shape in the RpcService generator](/docs/issues/0016-generator-notification-shape.md) — generated `void NotifyX(TRequest)` methods for one-way messages.
- [ ] [IRpcHost and the server broadcast call surface](/docs/issues/0020-rpc-host-broadcast.md) — one endpoint interface over `RpcClient`, `RpcPeer`, and `RpcServer`.

### Research

<!-- - [ ] [Title](/docs/issues/0000-slug.md) — one-line summary -->