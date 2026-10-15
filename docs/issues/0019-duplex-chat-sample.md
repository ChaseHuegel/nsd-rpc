# Improvement: Rewrite the generated chat sample on one duplex interface

- Type: improvement
- Status: done
- Workflow: ../specs/issues.md

## Problem

`Chat.Generated` used the retired per-method handler bases and the mixed
interface shape. It should use one `[RpcService]` interface whose invocation
shapes generate `ChatClient` and whose handler shapes generate `ChatHandler`.

## Acceptance criteria

- [x] `Chat.Generated` declares one `IChat` interface with both shapes.
- [x] The server subclasses `ChatHandler` and registers with `AddService`.
- [x] The client uses `ChatClient` and a partial `ChatHandler` that overrides only the chat notification. The relay uses `new ChatClient(peer)` per peer.
- [x] The sample runs end to end with the same output as before.
- [x] The docs pass updates [rpc-api](../../specs/rpc-api.md), the sample READMEs, and [architecture](../../architecture.md).
