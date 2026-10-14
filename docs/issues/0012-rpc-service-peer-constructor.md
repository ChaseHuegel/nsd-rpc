# Improvement: Peer-based constructor for generated service clients

- Type: improvement
- Status: open
- Workflow: ../specs/issues.md

## Problem

The generated service client takes an `RpcClient`. Server-to-client calls
go through `RpcServer.GetPeer(clientId)`, which returns an `RpcPeer`, so
the typed surface is unavailable on that path. A constructor variant that
takes an `RpcPeer` closes the gap.

## Acceptance criteria

- [ ] Generated service clients gain a constructor that takes an `RpcPeer`.
- [ ] Both constructors share one forwarding implementation.
- [ ] A test drives a server-to-client call through `RpcServer.GetPeer` with the typed client.
- [ ] The docs pass notes the variant in [rpc-api](../../specs/rpc-api.md).
