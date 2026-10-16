# Improvement: Cache the TCP frame delegate on the connection

- Type: improvement
- Status: done
- Workflow: ../specs/issues.md

## Problem

The TCP receive loop creates a new `RpcFrameHandler` delegate for each
socket read:

```csharp
if (!_decoder.TryPush(
        buffer.AsSpan(0, read), frame => _frameReceivedHandlers?.Invoke(frame)))
```

See [NsdRpc.Tcp/TcpConnection.cs:171](../../NsdRpc.Tcp/TcpConnection.cs:171).
The lambda captures `this`, so the compiler cannot cache it. An IL check of
the release build shows `ldftn` and `newobj RpcFrameHandler` inside the loop.

Each socket read allocates one delegate object. The receive loop is a hot
path. The rule in [coding-style](../specs/coding-style.md) forbids heap
allocation on hot paths.

## Acceptance criteria

- [x] `TcpConnection` creates the `RpcFrameHandler` once and stores it in a field. The receive loop passes that field to `TryPush`.
- [x] A release-build IL check shows no `newobj RpcFrameHandler` inside `ReceiveLoopAsync`. Record the check in this issue.
- [x] All TCP integration tests pass unchanged.

## Resolution

`TcpConnection` stores one `RpcFrameHandler` field, `_onFrame`, and the
receive loop passes it to `TryPush`.

Release-build IL check (dotnet-ildasm 0.12.2, `net10.0` Release): the
`ReceiveLoopAsync` state machine loads `_onFrame` (`ldfld`) and calls
`TryPush`. The method contains no `newobj RpcFrameHandler`. The only two
`newobj RpcFrameHandler` sites in the assembly are the `TcpConnection`
constructor and the `TcpClientConnection.ConnectAsync` subscription. Both
run once per connection.
