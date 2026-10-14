# Improvement: Add RpcClient and RpcServer composition

- Type: improvement
- Status: open
- Workflow: ../specs/issues.md

## Problem

The library needs two composition roots. One wraps one connection on the
client. One wraps many connections on the server. See
[../../specs/rpc-api.md](../../specs/rpc-api.md).

## Acceptance criteria

- [ ] `RpcClient` takes an `IRpcConnection`, a `RpcRegistry`, and optional `RpcOptions`. It exposes the peer call surface.
- [ ] The connection receive loop calls `HandleFrame`. The transport or the host calls `CheckTimeouts` with a timestamp from `RpcOptions.TimestampNanos`. Both paths are documented.
- [ ] `RpcServer` takes an `IRpcServerTransport`, a `RpcRegistry`, and optional `RpcOptions`. It accepts through `AcceptAsync` and owns one peer per connection, keyed by `ClientId`.
- [ ] `RpcServer` prunes peers whose connection reports `IsConnected == false` and faults their pending calls. A test drives the prune with a fake transport.
- [ ] `GetPeer` returns null for an unknown client id.
- [ ] Server-to-client calls and notifications work through `GetPeer`.
- [ ] A disconnect faults pending calls with `Unavailable` on both client and server. Tests use the in-memory pair and fake clock from issue 0004.
- [ ] The docs pass: [rpc-api](../../specs/rpc-api.md) gains `file:line` pointers, and [architecture](../../architecture.md) gains the module rows.
