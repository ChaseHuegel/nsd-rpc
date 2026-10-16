# Research: Allocation-free payload path

- Type: research
- Status: open
- Workflow: ../specs/issues.md

## Problem

Every message carries one payload allocation per direction.

- The sender allocates `new byte[size]` in `RpcPeer.SerializePayload`
  ([RpcPeer.cs:727](../../NsdRpc/Engine/RpcPeer.cs:727)).
- The receiver allocates the payload array in the generated `Unpack`.

The engine holds the payload bytes from the frame read to the handler call
or to the pending call. The frame span lives only during the transport
callback. The dispatch queue and the pending table outlive the callback.

Question: can the payload path avoid the per-message array?

## Acceptance criteria

- [ ] The investigation records the lifetime of the payload bytes from the transport read to the handler call.
- [ ] It compares at least two designs: a borrowed span into the transport buffer and a rented buffer with an explicit return.
- [ ] It records the effect on `IPayloadSerializer<T>` and on the needlefish generated API.
- [ ] It records the effect on `RpcPeer.HandleFrame`, `DispatchEntry`, and `PendingStreamCall.ReceiveChunk`.
- [ ] It ends with one recommendation and follow-up issues for the accepted design.
