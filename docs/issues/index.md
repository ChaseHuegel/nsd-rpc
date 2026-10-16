# Issue Index

The live tracker for this repository. Work Active Sprint items in order.
Read the format and workflow in [specs/issues.md](specs/issues.md) before you
add or work an issue.

## Active Sprint

### Bug

<!-- - [ ] [Title](/docs/issues/0000-slug.md) — one-line summary -->

### Improvement

<!-- - [ ] [Title](/docs/issues/0000-slug.md) — one-line summary -->

### Research

- [x] [Allocation-free payload path](/docs/issues/0030-allocation-free-payload-path.md) — can the per-message payload array go away?
- [x] [Idle TCP connection memory](/docs/issues/0042-idle-tcp-connection-memory.md) — measure one live connection and pick a smaller design.

## Backlog

### Bug

- [ ] [Coding style says the assert helper does not exist](/docs/issues/0054-coding-style-assert-helper-text.md) — the helper ships in `Invariant.cs`.

### Improvement

- [ ] [Generated codecs copy payloads byte by byte](/docs/issues/0027-generated-payload-memcpy.md) — an upstream needlefish defect.
- [ ] [Non-allocating payload access in needlefish](/docs/issues/0049-needlefish-payload-access.md) — the upstream feature that removes the payload array.
- [ ] [Borrow the payload span on the receive path](/docs/issues/0050-borrow-payload-span-on-receive.md) — stop allocating the receive payload array.
- [ ] [Serialize the payload into the frame on the send path](/docs/issues/0051-serialize-payload-into-frame.md) — stop allocating the send payload array.
- [ ] [Rent the TCP receive buffer per read](/docs/issues/0052-tcp-receive-buffer-per-read.md) — hold no receive buffer while a connection is idle.
- [ ] [Shrink the TCP frame decoder after idle](/docs/issues/0053-tcp-decoder-idle-shrink.md) — bound the decoder high-water mark.

### Research

<!-- - [ ] [Title](/docs/issues/0000-slug.md) — one-line summary -->