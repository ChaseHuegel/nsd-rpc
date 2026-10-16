# RPC Wire Protocol

One subject: the RPC wire protocol. The protocol models JSON-RPC 2.0. It runs
on the nsd wire format. See [nsd-schemas](nsd-schemas.md) for the format and
[codegen-pipeline](codegen-pipeline.md) for the build wiring.

Status: ratified on 2026-10-14. The implementation is complete. Issues
[0002](../issues/0002-rpc-envelope-schema.md) through
[0008](../issues/0008-rpc-streaming-cancellation.md) built it.

## Summary

One envelope carries a version, a kind, a correlation id, a method id, a
payload, and an error. Both peers can call. Both peers can handle calls.
Correlation by id replaces every FIFO reply rule. Chunk notifications extend
a call into a stream. A cancellation kind stops a call. See
[rpc-api](rpc-api.md) for the engine and handler API.

The JSON-RPC mapping is direct.

| JSON-RPC | Envelope |
| --- | --- |
| `jsonrpc` | `Version` |
| `id` | `Id` |
| `method` | `Method` as a method id |
| `params` | `Payload` of a request |
| `result` | `Payload` of a response |
| `error` | `Error` |

## Goals and non-goals

Goals:

- One wire envelope for all calls and one-way notifications.
- Streams. A call may receive chunk notifications before its final response.
- A symmetric protocol. Both peers use the same envelope.
- A stable error model that uses the JSON-RPC codes.
- TCP support in a sibling project. Users may provide their own transport.

Non-goals:

- No reliability layer. TCP guarantees order and delivery.
- No compression, batching, or multiplexed streams.
- No encryption or authentication. The transport provides both.
- No version negotiation in this version.

## Envelope

The envelope schema lives in
[NsdRpc/Nsd/Rpc.nsd](../../NsdRpc/Nsd/Rpc.nsd:1). The namespace is
`NsdRpc.Wire`.

```nsd
#version 1.0;
#namespace NsdRpc.Wire;

message RpcError
{
    int Code = 0;
    string Message = 1;
}

message Envelope
{
    byte Version = 0;      // RpcVersion. Current: 1. Zero is invalid.
    byte Kind = 1;         // RpcMessageKind. Zero is invalid.
    ulong Id = 2;          // Correlation id. Zero is a standalone notification.
    ulong Method = 3;      // Method id. Zero on responses and cancellations.
    byte[]? Payload = 4;   // nsd-serialized params, result, or chunk.
    RpcError? Error = 5;   // Present only on a failed response.
}
```

Wire facts:

- The minimum envelope is 26 bytes. It has a null payload and a null error.
- nsd arrays hold at most 65,535 values. A payload holds at most 65,535
  bytes. The largest envelope is 65,566 bytes. The largest frame is 65,570
  bytes. It has a full payload and a null error.
- Senders normalize an empty payload to null. A receiver treats a null
  payload and an empty payload as no data.
- Optional fields with no value are not encoded. A null payload costs zero
  bytes.

## Field semantics

| Field | Request | Response | Notification | Chunk | Cancellation |
| --- | --- | --- | --- | --- | --- |
| `Version` | 1 | 1 | 1 | 1 | 1 |
| `Kind` | Request | Response | Notification | Notification | Cancellation |
| `Id` | nonzero, caller-assigned | echo of the request id | 0 | the parent request id | the target request id, nonzero |
| `Method` | method id | 0 | method id | the parent method id | 0 |
| `Payload` | request, or null | result, or null | request, or null | chunk, or null | null |
| `Error` | null | null on success | null | null | null |

Rules:

- A request with `Id` 0 is invalid. Drop it and log it.
- A response with `Id` 0 is a response to a notification. Drop it and log it.
- A chunk with an unknown id is dropped and logged.
- A cancellation with an unknown id is dropped and logged.

## Message kinds

`RpcMessageKind : byte` is a hand-written enum. See
[NsdRpc/Protocol/RpcMessageKind.cs](../../NsdRpc/Protocol/RpcMessageKind.cs).
The wire field stays one byte. A generated nsd enum would cost two bytes for
no gain.

| Value | Name | Meaning |
| --- | --- | --- |
| 0 | `Unknown` | Invalid. A zero-initialized envelope fails validation loudly. |
| 1 | `Request` | A call. A response is required. |
| 2 | `Response` | The answer to one request. |
| 3 | `Notification` | A one-way message, or one chunk of a stream. |
| 4 | `Cancellation` | Stop the request named by `Id`. No response, no ack. |

An unknown kind value is dropped and logged.

## Payload rules

- A payload is an ordinary nsd message. Serialize it with
  `IPayloadSerializer<T>`. See [rpc-api](rpc-api.md). The transport sees only
  the envelope.
- A payload type needs no transport registration.
- A null payload means no data.
- `MaxFrameBytes` bounds one frame, including the 4-byte length prefix. The
  default is 65,570, the size of the largest valid frame. A frame one byte
  above the default fails.
- The 65,535-byte payload cap is a format limit. It is not configurable.

## Error codes

`RpcError.Code` uses the JSON-RPC numeric space. The codes live in
[NsdRpc/Protocol/RpcErrorCode.cs](../../NsdRpc/Protocol/RpcErrorCode.cs).

| Code | Name | Use |
| --- | --- | --- |
| -32600 | `InvalidRequest` | The envelope is malformed for its kind. |
| -32601 | `MethodNotFound` | No handler is registered, or the direction is wrong. |
| -32602 | `InvalidParams` | The payload did not decode. |
| -32603 | `InternalError` | The handler threw. The wire carries no internal detail. |
| -32001 | `RequestTimeout` | The call passed its deadline. Local only. |
| -32002 | `PermissionDenied` | Reserved. No authorization hook exists yet. |
| -32003 | `Unavailable` | The connection is down or gone. Local only. |
| -32004 | `Busy` | The receiver is at capacity. Also used for a full stream queue. |
| -32005 | `Cancelled` | The caller's token fired. Local only, never on the wire. |

Code space:

- The library owns -32000 through -32099.
- App codes start at -32100 and go down.
- Handlers may fail with app codes only.
- The engine owns every library code. Handlers cannot emit them.

## Method identity

- `RpcMethodAttribute` holds a `ulong` method id and a direction. See
  [rpc-api](rpc-api.md).
- A method id is a wire contract. Never change it between builds. The app
  keeps one constants class for its ids.
- Zero is never a valid method id. Responses and cancellations use zero for
  no method.
- The registry fails fast on a zero id or a duplicate id.

## Streaming

A stream is one request, zero or more chunk notifications, and one final
response, in that order, on one connection.

- A chunk carries the parent request id in `Id`.
- A chunk carries the parent method id in `Method`. Frames stay
  self-describing in logs.
- The chunk type is a wire contract. The client names it in the typed stream
  call. See [rpc-api](rpc-api.md).
- Each chunk resets the call deadline. The timeout is an idle timeout.
- Only the response completes the call.
- The transport preserves order. The peer processes frames in order on one
  pump. Chunks arrive in order. No sequence numbers.
- A chunk that arrives after completion has an unknown id. It is dropped and
  logged.

## Cancellation

The caller sends one cancellation frame when a call ends locally by token or
by deadline. The send is best-effort. There is no ack.

- A cancellation with `Id` 0, a nonzero `Method`, or a non-null `Payload` is
  invalid. Drop it and log it.
- A cancellation with an unknown id is dropped and logged.
- A cancellation with a known id marks the request cancelled. An undispatched
  request is never dispatched. A running handler finishes, but the peer drops
  its response and chunks at send.
- A late response at the caller has an unknown id. The caller drops it.
- Ids are never reused on a connection. This rule makes late frames safe. A
  stale frame cannot match a new call.

## Versioning and reliability

- `Version` is 1.
- A receiver with another version drops the frame and closes the connection.
  A reply in an unknown format is impossible. A later change may negotiate
  the version.
- Field ids are written in ascending order. Add fields. Do not reuse ids. The
  reader stops after it reads every field it knows, so an appended field with
  a new id is ignored. The nsd format has no field-length prefix, so a reader
  cannot skip an unknown field that sits before a known field. Do not reorder
  fields.
- A future `RpcError` data field is the first planned extension.
