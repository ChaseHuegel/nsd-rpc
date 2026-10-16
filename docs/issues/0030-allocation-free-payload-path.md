# Research: Allocation-free payload path

- Type: research
- Status: done
- Workflow: ../specs/issues.md

## Problem

Every message carries one payload allocation per direction.

- The sender allocates `new byte[size]` in `RpcPeer.SerializePayload`
  ([RpcPeer.cs:771](../../NsdRpc/Engine/RpcPeer.cs:771)).
- The receiver allocates the payload array in the generated `Unpack`.

The engine holds the payload bytes from the frame read to the handler call
or to the pending call. The frame span lives only during the transport
callback. The dispatch queue and the pending table outlive the callback.

Question: can the payload path avoid the per-message array?

## Acceptance criteria

- [x] The investigation records the lifetime of the payload bytes from the
  transport read to the handler call.
- [x] It compares at least two designs: a borrowed span into the transport
  buffer and a rented buffer with an explicit return.
- [x] It records the effect on `IPayloadSerializer<T>` and on the needlefish
  generated API.
- [x] It records the effect on `RpcPeer.HandleFrame`, `DispatchEntry`, and
  `PendingStreamCall.ReceiveChunk`.
- [x] It ends with one recommendation and follow-up issues for the accepted
  design.

## Payload lifetime

The frame bytes come from the transport. The TCP transport is the only
shipped push transport. Its lifetime is short.

1. `TcpConnection.ReceiveLoopAsync` rents one byte array for the connection
   ([TcpConnection.cs:212](../../NsdRpc.Tcp/TcpConnection.cs:212)). It reads
   into that array and reuses it for every read.
2. `TcpFrameDecoder` owns a second byte array
   ([TcpFrameDecoder.cs:16](../../NsdRpc.Tcp/TcpFrameDecoder.cs:16)). It
   copies each read into the array. It calls the frame handler with a span
   over the array
   ([TcpFrameDecoder.cs:82](../../NsdRpc.Tcp/TcpFrameDecoder.cs:82)). It
   compacts the array after it emits every complete frame. The span stays
   stable during one handler call.
3. `RpcFrameHandler` states that the span is valid only during the call
   ([RpcFrameHandler.cs:4](../../NsdRpc/Transport/RpcFrameHandler.cs:4)).

So a payload span into the frame is valid only inside `RpcPeer.HandleFrame`.

Inside `HandleFrame`
([RpcPeer.cs:292](../../NsdRpc/Engine/RpcPeer.cs:292)):

- `Envelope.Deserialize` copies the payload into a new array
  ([RpcPeer.cs:321](../../NsdRpc/Engine/RpcPeer.cs:321)). The generated
  `Unpack` allocates the array and fills it.
- The array then takes one of two routes.

| Message | Holder after the read | Last use | Outlives the callback |
|---|---|---|---|
| Request | `DispatchEntry.Payload` on the dispatch queue | the dispatch worker deserializes the payload | Yes |
| Notification | `DispatchEntry.Payload` on the dispatch queue | the dispatch worker deserializes the payload | Yes |
| Response | the local `CompleteResult` call | `TryDeserialize` | No |
| Chunk | the local `ReceiveChunk` call | `TryDeserialize` and the sink | No |
| Cancellation | none | none | No |

The request path enqueues the array at
[RpcPeer.cs:943](../../NsdRpc/Engine/RpcPeer.cs:943). The notification path
enqueues it at
[RpcPeer.cs:1031](../../NsdRpc/Engine/RpcPeer.cs:1031). The dispatch worker
runs after `HandleFrame` returns. The dispatch worker then deserializes the
payload from the array
([HandlerEntryOfT.cs:27](../../NsdRpc/Handlers/HandlerEntryOfT.cs:27)).
The array must survive that wait.

The response path calls `CompleteResult` at
[RpcPeer.cs:997](../../NsdRpc/Engine/RpcPeer.cs:997). The chunk path calls
`ReceiveChunk` at
[RpcPeer.cs:1071](../../NsdRpc/Engine/RpcPeer.cs:1071). Both calls deserialize
the payload on the spot. The array dies before `HandleFrame` returns.

The send side is shorter.

- `SerializePayload` allocates the array
  ([RpcPeer.cs:762](../../NsdRpc/Engine/RpcPeer.cs:762)).
- `SendEnvelope` rents the frame from `ArrayPool<byte>` and copies the array
  into the frame byte by byte
  ([RpcPeer.cs:776](../../NsdRpc/Engine/RpcPeer.cs:776)). Issue
  [0027](0027-generated-payload-memcpy.md) tracks that copy loop.
- The transport takes the frame or copies it. The payload array dies after
  the copy.

One message therefore allocates one payload array for each direction. The
frame is pooled and does not count.

## The generated payload barrier

Both designs need the payload bytes at a known position without the
`new byte[]`. The generated `Envelope` gives only a `byte[]? Payload` field.
The generated `Unpack` always allocates that array when the field is present.

The library may not take the envelope apart by hand.
[nsd-schemas](../specs/nsd-schemas.md) says to call the generated methods and
not to re-derive the wire rules. So the engine cannot find the payload slice
from the frame alone. This is the hard limit for every receive design.

The same limit blocks the send side. The generated `Envelope.SerializeInto`
writes a `byte[]` payload. It has no path that takes the payload bytes as a
span. The `Needlefish.Generators` 1.2.0 package has one option,
`NeedlefishPartial`. It has no option for a payload view or a payload
writer.

Without an upstream feature, the engine keeps the arrays. That feature is
the gate for the accepted design.

## Design A: borrowed span into the transport frame

`HandleFrame` keeps the frame span and passes a payload sub-span to the
consumer. The path allocates nothing and copies nothing.

- Response and chunk: the consumer deserializes inside `HandleFrame`. The
  borrow is safe.
- Request and notification: the payload enters the dispatch queue. The
  worker runs after `HandleFrame` returns. The borrow is not safe. The
  worker would read a buffer that the decoder already reused.

So Design A removes the array on the response and chunk paths only. The
request and notification paths must copy first. Design A cannot stand alone.

Design A also requires that the consume path never awaits. `CompleteResult`
and `ReceiveChunk` deserialize synchronously today. A later asynchronous
handoff would break the borrow.

## Design B: rented buffer with an explicit return

The engine copies the payload slice into a pooled array at enqueue. The
dispatch worker deserializes from the pooled array. The engine returns the
array after the deserialize or on any terminal path.

- The copy costs one pass over the payload bytes. The array is pooled, so
  the steady state allocates no managed memory.
- Ownership becomes explicit. Every terminal path must return the buffer:
  dispatched, cancelled before dispatch
  ([RpcPeer.cs:1211](../../NsdRpc/Engine/RpcPeer.cs:1211)), no handler
  ([RpcPeer.cs:1218](../../NsdRpc/Engine/RpcPeer.cs:1218)), handler
  exception, and fault. `FaultPending` clears the queue and must return its
  buffers ([RpcPeer.cs:442](../../NsdRpc/Engine/RpcPeer.cs:442)).
- A missed return leaks a pooled array. An assert can catch a double return.
- Design B needs the payload slice source from the generated parser, the
  same as Design A.

Design B covers every path. It needs one copy on the queued paths.

## Effect on the interfaces

### IPayloadSerializer<T>

- `TryDeserialize` already takes a `ReadOnlySpan<byte>`
  ([IPayloadSerializer.cs:15](../../NsdRpc/Engine/IPayloadSerializer.cs:15)).
  It fits both designs. The receive path needs no interface change.
- The send path needs one overload that writes into a caller span. An
  example is `void Serialize(in T value, ref byte[] buffer, int offset,
  int size)`. The default wrapper already binds the generated
  `SerializeInto(Span<byte>, int)`. The overload maps to that method.

### Needlefish generated API

- Receive: an `Unpack` path that reports the payload range and does not
  allocate. One shape is an `Unpack` overload with `out int payloadStart`
  and `out int payloadLength`. The exact shape is an upstream decision.
- Send: an `Envelope` serialize path whose payload bytes come as a span or a
  writer. Then the payload field header and the payload bytes land in the
  frame in one pass.
- Both are upstream features. Issue
  [0027](0027-generated-payload-memcpy.md) is the precedent for tracking an
  upstream change from this repository.

### RpcPeer.HandleFrame

- The engine stops reading `Envelope.Payload`.
- Response and chunk: pass the payload slice to a span overload of
  `CompleteResult` and `ReceiveChunk`.
- Request and notification: copy the slice into a pooled buffer before the
  enqueue.
- The version check and the kind switch do not change.
- `HandleFrame` must not hold the frame span in a field, a closure, or a
  task. The span dies at the return.

### DispatchEntry

- `DispatchEntry` carries the pooled array, the payload length, and a pooled
  flag ([DispatchEntry.cs:17](../../NsdRpc/Engine/DispatchEntry.cs:17)). The
  length matters because a pooled array is longer than the payload.
- The entry gains one owner. The dispatch worker returns the buffer after
  `HandlerEntry.ExecuteAsync` deserializes the request.
- The cancelled-before-dispatch branch and `FaultPending` return the buffers
  of the queued entries.

### PendingStreamCall.ReceiveChunk

- The signature changes from `byte[]? payload` to `ReadOnlySpan<byte>
  payload` ([PendingStreamCall.cs:23](../../NsdRpc/Engine/PendingStreamCall.cs:23)).
- The method deserializes the chunk and calls the sink in the same call. The
  sink copies the `TChunk` value into its queue
  ([SessionChunkSink.cs:16](../../NsdRpc/Engine/SessionChunkSink.cs:16)). The
  span is safe.
- `PendingCallBase.ReceiveChunk`
  ([PendingCallBase.cs:29](../../NsdRpc/Engine/PendingCallBase.cs:29)) and
  `PendingCall.CompleteResult`
  ([PendingCallBase.cs:23](../../NsdRpc/Engine/PendingCallBase.cs:23)) change
  in the same way.

## Recommendation

Use one rule: borrow the payload when it dies inside `HandleFrame`, and
rent and return it when it outlives `HandleFrame`. Use one more rule on the
send path: build the payload in the frame.

1. Response and chunk: pass a borrowed payload span. No allocation and no
   copy.
2. Request and notification: copy the payload span into a pooled buffer at
   enqueue. Return the buffer after the handler deserializes the request.
3. Send: serialize the payload into the rented frame. Delete the payload
   array in `SerializePayload`.

Steps 1 and 3 remove the payload array with no copy. Step 2 removes the
per-message allocation with one copy. The copy is necessary because the
dispatch queue outlives the frame span.

All three steps need the upstream payload-access feature. Without it, the
engine keeps the arrays. The copy in step 2 is the price of the
serial-per-peer dispatch queue.

The distinct field allocations inside a payload do not change. A payload
with a string or an array still allocates those values. This work removes
only the payload container.

Follow-up issues:

- [0049](../issues/0049-needlefish-payload-access.md) — the upstream
  needlefish feature.
- [0050](../issues/0050-borrow-payload-span-on-receive.md) — the receive
  path.
- [0051](../issues/0051-serialize-payload-into-frame.md) — the send path.
