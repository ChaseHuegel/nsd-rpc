# Improvement: Serialize the payload into the frame on the send path

- Type: improvement
- Status: open
- Workflow: ../specs/issues.md

## Problem

The send path allocates one payload array per message. `RpcPeer.SerializePayload`
allocates the array
([RpcPeer.cs:771](../../NsdRpc/Engine/RpcPeer.cs:771)). `SendEnvelope` then
copies the array into the pooled frame
([RpcPeer.cs:776](../../NsdRpc/Engine/RpcPeer.cs:776)).

Issue [0030](0030-allocation-free-payload-path.md) picks a design that
builds the payload in the frame. This issue needs the upstream feature in
issue [0049](0049-needlefish-payload-access.md) for the send side.

## Acceptance criteria

- [ ] `RpcPeer.SerializePayload` returns no payload array. The serializer
  writes the payload into the rented frame.
- [ ] `IPayloadSerializer<T>` gains one overload that writes into a caller
  span. The default wrapper maps it to the generated serialize method.
- [ ] A test proves a send allocates no payload array.
- [ ] The round-trip wire bytes do not change. The wire-contract tests pass
  unchanged.
- [ ] A measurement records the send allocation of one message before and
  after the change. Record the numbers in this issue.
