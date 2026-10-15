# Improvement: Rewrite the generated chat sample on one duplex interface

- Type: improvement
- Status: open
- Workflow: ../specs/issues.md

## Problem

`Chat.Generated` uses the retired per-method handler bases and the mixed
interface shape. It should use one `[RpcService]` interface whose invocation
shapes generate `ChatClient` and whose handler shapes generate `ChatHandler`.

## Acceptance criteria

- [ ] `Chat.Generated` declares one `IChat` interface with both shapes.
- [ ] The server subclasses `ChatHandler` and registers with `AddService`.
- [ ] The client uses `ChatClient`. The relay uses `new ChatClient(peer)` per peer.
- [ ] The sample runs end to end with the same output as before.
- [ ] The docs pass updates [rpc-api](../../specs/rpc-api.md), the sample READMEs, and [architecture](../../architecture.md).
