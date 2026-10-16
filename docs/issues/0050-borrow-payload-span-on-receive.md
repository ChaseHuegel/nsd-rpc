# Improvement: Borrow the payload span on the receive path

- Type: improvement
- Status: open
- Workflow: ../specs/issues.md

## Problem

The receive path allocates one payload array per message. The generated
`Unpack` copies the payload into the array. Issue
[0030](0030-allocation-free-payload-path.md) records the lifetime and picks
a design.

The design borrows the payload span where the payload dies inside
`RpcPeer.HandleFrame`. It copies into a pooled buffer where the payload must
outlive the callback. This issue needs the upstream feature in issue
[0049](0049-needlefish-payload-access.md).

## Acceptance criteria

- [ ] `RpcPeer.HandleFrame` passes a payload span to `CompleteResult` and
  `ReceiveChunk` for a response and a chunk. The path allocates no payload
  array.
- [ ] The request and notification paths copy the payload span into a pooled
  buffer at enqueue.
- [ ] The engine returns every pooled payload buffer on every terminal path:
  dispatched, cancelled before dispatch, no handler, handler exception, and
  fault. An always-on assert catches a double return.
- [ ] A test proves the response and chunk paths allocate no payload array.
  A test proves the request and notification paths allocate no fresh array
  per message.
- [ ] The timeout, cancellation, fault, and drop tests pass unchanged. The
  round-trip wire bytes do not change.
