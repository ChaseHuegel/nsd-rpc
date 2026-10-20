# Issue Index

The live tracker for this repository. Work Active Sprint items in order.
Read the format and workflow in [specs/issues.md](specs/issues.md) before you
add or work an issue.

## Active Sprint

### Bug

- [x] [Canceled inbound entries keep a disposed token source](/docs/issues/0063-canceled-inbound-disposed-source.md) — remove the entry and tolerate the disposed source on disconnect.
- [x] [Map every payload decode failure to InvalidParams](/docs/issues/0062-decode-failure-mapping.md) — a corrupt payload must not look like a handler failure.
- [x] [Coding style says the assert helper does not exist](/docs/issues/0054-coding-style-assert-helper-text.md) — the helper ships in `Invariant.cs`.

### Improvement

- [x] [Reliable send queue with a backlog policy](/docs/issues/0056-send-backlog-policy.md) — never drop an accepted frame, close a peer that cannot drain.
- [x] [Idle timeout and disconnect reasons](/docs/issues/0057-idle-timeout-and-reasons.md) — detect a half-open peer and report why it closed.
- [x] [Connection metrics](/docs/issues/0058-connection-metrics.md) — frames, bytes, queue depth, and rejected sends.
- [x] [Connect timeout and bind address](/docs/issues/0059-connect-timeout-and-bind-address.md) — bound connect and choose the listen interface.
- [x] [Send batching](/docs/issues/0060-send-batching.md) — one write per interval instead of one write per frame.
- [x] [Peer endpoint, peer close, and log sink](/docs/issues/0061-peer-endpoint-and-log-sink.md) — a host reads the endpoint, disconnects one peer, and attaches a logger.
- [x] [Server targets peers, not a broadcast](/docs/issues/0055-server-peer-targeting.md) — remove the server broadcast surface and select peers explicitly.
- [x] [Rent the TCP receive buffer per read](/docs/issues/0052-tcp-receive-buffer-per-read.md) — hold no receive buffer while a connection is idle.
- [x] [Shrink the TCP frame decoder after idle](/docs/issues/0053-tcp-decoder-idle-shrink.md) — bound the decoder high-water mark.

### Research

<!-- - [ ] [Title](/docs/issues/0000-slug.md) — one-line summary -->

## Backlog

### Bug

- [x] [A request without a dispatch hook leaks its inbound entry](/docs/issues/0064-no-hook-inbound-leak.md) — the fallback response must remove the entry.

### Improvement

- [ ] [Generated codecs copy payloads byte by byte](/docs/issues/0027-generated-payload-memcpy.md) — an upstream needlefish defect.
- [ ] [Non-allocating payload access in needlefish](/docs/issues/0049-needlefish-payload-access.md) — the upstream feature that removes the payload array.
- [ ] [Borrow the payload span on the receive path](/docs/issues/0050-borrow-payload-span-on-receive.md) — stop allocating the receive payload array.
- [ ] [Serialize the payload into the frame on the send path](/docs/issues/0051-serialize-payload-into-frame.md) — stop allocating the send payload array.

### Research

<!-- - [ ] [Title](/docs/issues/0000-slug.md) — one-line summary -->