# Improvement: Peer-based constructor for generated service clients

- Type: improvement
- Status: done
- Workflow: ../specs/issues.md

## Problem

The generated service client took an `RpcClient`. Server-to-client calls
go through `RpcServer.GetPeer(clientId)`, which returns an `RpcPeer`, so
the typed surface was unavailable on that path. A constructor variant that
takes an `RpcPeer` closes the gap.

## Acceptance criteria

- [x] Generated service clients gain a constructor that takes an `RpcPeer`.
- [x] Both constructors share one forwarding implementation through the new `IRpcCaller` interface, which `RpcClient` and `RpcPeer` both implement.
- [x] A compile-and-run test drives a server-to-client call through `RpcServer.GetPeer` with the typed client.
- [x] The docs pass notes the variant in [rpc-api](../../specs/rpc-api.md).
