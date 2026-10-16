# Issue Index

The live tracker for this repository. Work Active Sprint items in order.
Read the format and workflow in [specs/issues.md](specs/issues.md) before you
add or work an issue.

## Active Sprint

### Bug

- [x] [TcpServerTransport leaks accepted connections](/docs/issues/0031-tcp-accepted-connection-leak.md) — a disconnected client stays in the accepted list.
- [x] [Frame decoder rejects valid data near the frame cap](/docs/issues/0032-frame-decoder-false-rejection.md) — a read that spans a frame boundary kills the connection.
- [x] [The TCP accept loop stalls on a silent client](/docs/issues/0033-tcp-handshake-accept-stall.md) — handshakes run serially and have no deadline.
- [x] [MaxFrameBytes semantics disagree and the default blocks valid frames](/docs/issues/0034-max-frame-bytes-semantics.md) — the peer and the decoder use different bounds.
- [x] [Envelope version mismatch does not close the connection](/docs/issues/0035-envelope-version-mismatch-close.md) — the peer faults calls and leaves the socket open.
- [x] [The generated reader does not skip unknown fields](/docs/issues/0036-generated-unknown-field-skip.md) — trailing bytes scan as field ids.
- [x] [A chunk for a non-stream call resets its deadline](/docs/issues/0037-non-stream-chunk-deadline-reset.md) — a dropped chunk extends the timeout.
- [x] [A malformed chunk completes the call and keeps the pending entry](/docs/issues/0038-malformed-chunk-pending-entry.md) — the slot leaks until a response or a timeout.
- [x] [default(RpcResult<T>).ErrorMessage is null](/docs/issues/0039-default-result-error-message.md) — the annotation says non-null.
- [x] [The TCP send loop drops the remainder of a partial send](/docs/issues/0040-tcp-partial-send.md) — a stream send can complete partly and the frame is truncated.
- [x] [A notification flood grows the dispatch queue without a bound](/docs/issues/0041-notification-queue-bound.md) — notifications bypass the inbound cap and pin payload arrays.

### Improvement

- [x] [IRpcHost and the server broadcast call surface](/docs/issues/0020-rpc-host-broadcast.md) — one endpoint interface over `RpcClient`, `RpcPeer`, and `RpcServer`.
- [x] [Cache the TCP frame delegate on the connection](/docs/issues/0022-cache-tcp-frame-delegate.md) — the receive loop allocates a delegate per socket read.
- [x] [Read the frozen handler registry without a lock](/docs/issues/0023-lock-free-frozen-registry.md) — every dispatch takes the registry lock.
- [x] [Remove the extra copy on the TCP send path](/docs/issues/0024-tcp-send-copy-elimination.md) — `TrySend` copies each frame into a fresh array.
- [x] [Avoid the linked cancellation source per dispatch](/docs/issues/0025-dispatch-cancellation-token-source.md) — each inbound request allocates a linked token graph.
- [x] [Reuse locks and payload sizes in call setup](/docs/issues/0026-call-setup-lock-and-size-reuse.md) — call setup takes the pending lock three times.
- [x] [Allocate the peer timeout scratch lazily](/docs/issues/0028-lazy-peer-timeout-scratch.md) — each idle peer holds 8 kilobytes it never uses.
- [x] [Prove hot-path allocations with real payloads](/docs/issues/0029-hot-path-allocation-tests.md) — the current tests hide the codec allocation.
- [x] [Skip timeout scans with a per-peer deadline watermark](/docs/issues/0043-timeout-scan-watermark.md) — a poll rescans every pending call of every peer.
- [x] [Cache the call-cancelled delegate in RpcPeer](/docs/issues/0044-cached-cancel-delegate.md) — the instance method group allocates per call.
- [x] [Build no drop string while the trace switch is off](/docs/issues/0045-trace-switch-guard.md) — drops allocate a string although the default switch blocks the event.
- [x] [Reuse the stream consumer signal instead of a TCS per park](/docs/issues/0046-stream-wait-signal-reuse.md) — a drained queue allocates one signal per wait.

### Research

<!-- - [ ] [Title](/docs/issues/0000-slug.md) — one-line summary -->

## Backlog

### Bug

- [x] [The server transport drops a frame that arrives before the peer subscribes](/docs/issues/0047-tcp-early-frame-drop.md) — the receive loop starts before the frame handler subscribes.
- [ ] [The generated reader cannot reject an unknown field id among known ids](/docs/issues/0048-generated-unknown-id-rejection.md) — the nsd format cannot skip an unknown field.

### Improvement

- [ ] [Generated codecs copy payloads byte by byte](/docs/issues/0027-generated-payload-memcpy.md) — an upstream needlefish defect.

### Research

- [ ] [Allocation-free payload path](/docs/issues/0030-allocation-free-payload-path.md) — can the per-message payload array go away?
- [ ] [Idle TCP connection memory](/docs/issues/0042-idle-tcp-connection-memory.md) — measure one live connection and pick a smaller design.