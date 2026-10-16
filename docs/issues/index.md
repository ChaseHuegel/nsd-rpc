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

- [ ] [TcpServerTransport leaks accepted connections](/docs/issues/0031-tcp-accepted-connection-leak.md) — a disconnected client stays in the accepted list.
- [ ] [Frame decoder rejects valid data near the frame cap](/docs/issues/0032-frame-decoder-false-rejection.md) — a read that spans a frame boundary kills the connection.
- [ ] [The TCP accept loop stalls on a silent client](/docs/issues/0033-tcp-handshake-accept-stall.md) — handshakes run serially and have no deadline.
- [ ] [MaxFrameBytes semantics disagree and the default blocks valid frames](/docs/issues/0034-max-frame-bytes-semantics.md) — the peer and the decoder use different bounds.
- [ ] [Envelope version mismatch does not close the connection](/docs/issues/0035-envelope-version-mismatch-close.md) — the peer faults calls and leaves the socket open.
- [ ] [The generated reader does not skip unknown fields](/docs/issues/0036-generated-unknown-field-skip.md) — trailing bytes scan as field ids.
- [ ] [A chunk for a non-stream call resets its deadline](/docs/issues/0037-non-stream-chunk-deadline-reset.md) — a dropped chunk extends the timeout.
- [ ] [A malformed chunk completes the call and keeps the pending entry](/docs/issues/0038-malformed-chunk-pending-entry.md) — the slot leaks until a response or a timeout.
- [ ] [default(RpcResult<T>).ErrorMessage is null](/docs/issues/0039-default-result-error-message.md) — the annotation says non-null.
- [ ] [The TCP send loop drops the remainder of a partial send](/docs/issues/0040-tcp-partial-send.md) — a stream send can complete partly and the frame is truncated.
- [ ] [A notification flood grows the dispatch queue without a bound](/docs/issues/0041-notification-queue-bound.md) — notifications bypass the inbound cap and pin payload arrays.

### Improvement

- [x] [Notification shape in the RpcService generator](/docs/issues/0016-generator-notification-shape.md) — generated `void NotifyX(TRequest)` methods for one-way messages.
- [ ] [IRpcHost and the server broadcast call surface](/docs/issues/0020-rpc-host-broadcast.md) — one endpoint interface over `RpcClient`, `RpcPeer`, and `RpcServer`.
- [ ] [Cache the TCP frame delegate on the connection](/docs/issues/0022-cache-tcp-frame-delegate.md) — the receive loop allocates a delegate per socket read.
- [ ] [Read the frozen handler registry without a lock](/docs/issues/0023-lock-free-frozen-registry.md) — every dispatch takes the registry lock.
- [ ] [Remove the extra copy on the TCP send path](/docs/issues/0024-tcp-send-copy-elimination.md) — `TrySend` copies each frame into a fresh array.
- [ ] [Avoid the linked cancellation source per dispatch](/docs/issues/0025-dispatch-cancellation-token-source.md) — each inbound request allocates a linked token graph.
- [ ] [Reuse locks and payload sizes in call setup](/docs/issues/0026-call-setup-lock-and-size-reuse.md) — call setup takes the pending lock three times.
- [ ] [Generated codecs copy payloads byte by byte](/docs/issues/0027-generated-payload-memcpy.md) — an upstream needlefish defect.
- [ ] [Allocate the peer timeout scratch lazily](/docs/issues/0028-lazy-peer-timeout-scratch.md) — each idle peer holds 8 kilobytes it never uses.
- [ ] [Prove hot-path allocations with real payloads](/docs/issues/0029-hot-path-allocation-tests.md) — the current tests hide the codec allocation.
- [ ] [Skip timeout scans with a per-peer deadline watermark](/docs/issues/0043-timeout-scan-watermark.md) — a poll rescans every pending call of every peer.
- [ ] [Cache the call-cancelled delegate in RpcPeer](/docs/issues/0044-cached-cancel-delegate.md) — the instance method group allocates per call.
- [ ] [Build no drop string while the trace switch is off](/docs/issues/0045-trace-switch-guard.md) — drops allocate a string although the default switch blocks the event.
- [ ] [Reuse the stream consumer signal instead of a TCS per park](/docs/issues/0046-stream-wait-signal-reuse.md) — a drained queue allocates one signal per wait.

### Research

- [ ] [Allocation-free payload path](/docs/issues/0030-allocation-free-payload-path.md) — can the per-message payload array go away?
- [ ] [Idle TCP connection memory](/docs/issues/0042-idle-tcp-connection-memory.md) — measure one live connection and pick a smaller design.