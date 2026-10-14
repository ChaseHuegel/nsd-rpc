# Issue Index

The live tracker for this repository. Work Active Sprint items in order.
Read the format and workflow in [specs/issues.md](specs/issues.md) before you
add or work an issue.

## Active Sprint

### Bug

- [x] [Deserialize reads out of bounds on truncated buffers](/docs/issues/0001-malformed-buffer-deserialize-overread.md) — generated deserializers over-read malformed input.

### Improvement

- [ ] [Add the RPC envelope schema and pin the wire contract](/docs/issues/0002-rpc-envelope-schema.md) — the schema, the kinds, and the error codes, with wire tests.
- [ ] [Add Layer 0 transport abstractions and options](/docs/issues/0003-rpc-transport-abstractions.md) — the connection interfaces and `RpcOptions`.
- [ ] [Add RpcPeer, the protocol engine](/docs/issues/0004-rpc-peer-engine.md) — correlation, timeouts, cancellation, and faults.
- [ ] [Add the handler API, registry, and dispatcher](/docs/issues/0005-rpc-handler-dispatch.md) — attributes, scanning, results, and payload codecs.
- [ ] [Add RpcClient and RpcServer composition](/docs/issues/0006-rpc-client-server.md) — the two composition roots and their lifecycle.
- [ ] [Add the NsdRpc.Tcp transport project](/docs/issues/0007-tcp-transport.md) — framing, sockets, and the handshake.
- [ ] [Prove streaming and cancellation end to end](/docs/issues/0008-rpc-streaming-cancellation.md) — chunks, keep-alive, and every cancellation case.

### Research

<!-- - [ ] [Title](/docs/issues/0000-slug.md) — one-line summary -->

## Backlog

### Bug

<!-- - [ ] [Title](/docs/issues/0000-slug.md) — one-line summary -->

### Improvement

- [ ] [nsdc generated output lacks the nullable directive](/docs/issues/0009-nsdc-nullable-directive.md) — the build prepends `#nullable enable`; remove when nsdc emits it.

### Research

<!-- - [ ] [Title](/docs/issues/0000-slug.md) — one-line summary -->