# Improvement: Add RpcClient and RpcServer composition

- Type: improvement
- Status: done
- Workflow: ../specs/issues.md

## Problem

The library needed two composition roots. One wraps one connection on the
client. One wraps many connections on the server. See
[../../specs/rpc-api.md](../../specs/rpc-api.md).

## Acceptance criteria

- [x] `RpcClient` takes an `IRpcConnection`, a `RpcRegistry`, and optional `RpcOptions`. It exposes the peer call surface.
- [x] The connection receive loop calls `HandleFrame`. The transport or the host calls `CheckTimeouts` with a timestamp from `RpcOptions.TimestampNanos`. Both paths are documented.
- [x] `RpcServer` takes an `IRpcServerTransport`, a `RpcRegistry`, and optional `RpcOptions`. It accepts through `AcceptAsync` and owns one peer per connection, keyed by `ClientId`.
- [x] `RpcServer` prunes peers whose connection reports `IsConnected == false` and faults their pending calls. A test drives the prune with a fake transport.
- [x] `GetPeer` returns null for an unknown client id.
- [x] Server-to-client calls and notifications work through `GetPeer`.
- [x] A disconnect faults pending calls with `Unavailable` on both client and server. Tests use the in-memory pair and fake clock from issue 0004.
- [x] The docs pass: [rpc-api](../../specs/rpc-api.md) gains `file:line` pointers, and [architecture](../../architecture.md) gains the module rows.
