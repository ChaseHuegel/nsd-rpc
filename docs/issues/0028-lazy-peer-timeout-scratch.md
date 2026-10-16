# Improvement: Allocate the peer timeout scratch lazily

- Type: improvement
- Status: done
- Workflow: ../specs/issues.md

## Problem

`RpcPeer` allocates the expired-call scratch array in the constructor:

```csharp
_expiredScratch = new PendingCallBase[_options.MaxPendingCalls];
```

See [NsdRpc/Engine/RpcPeer.cs:50](../../NsdRpc/Engine/RpcPeer.cs:50). The
default `MaxPendingCalls` is 1024
([RpcOptions.cs:16](../../NsdRpc/RpcOptions.cs:16)). Each peer holds 8
kilobytes of unused reference array until the first timeout check. A server
with 10,000 idle connections holds about 80 megabytes.

`CheckTimeouts` is the only user of the scratch. `FaultPending` rents its
own snapshot already.

## Acceptance criteria

- [x] A new peer allocates no scratch array. A test measures the allocations of a default peer construction and asserts a bound below 8 kilobytes.
- [x] The first `CheckTimeouts` call allocates the scratch once. Later calls reuse it.
- [x] Timeout and fault tests pass unchanged.

## Resolution

`RpcPeer` allocates the scratch array on the first `CheckTimeouts` call and
reuses it. The construction test records an average below 4,096 bytes per
peer. The scratch test proves later checks allocate nothing.
