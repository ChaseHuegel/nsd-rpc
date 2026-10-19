# Improvement: Idle timeout and disconnect reasons

- Type: improvement
- Status: open
- Workflow: ../specs/issues.md

## Problem

`TcpConnection.WaitForDataAsync` awaits the socket without a deadline
([TcpConnection.cs:259](../../NsdRpc.Tcp/TcpConnection.cs:259)). A peer that
disappears without a FIN keeps the connection and its buffers forever.

`Disconnected` is `Action` with no reason
([IRpcConnection.cs:22](../../NsdRpc/Transport/IRpcConnection.cs:22)). A host
cannot tell an idle timeout from a write failure, a backlog drop, or an
intentional close.

The handshake has a deadline. The receive loop does not.

## Acceptance criteria

- [ ] `RpcDisconnectReason` has `PeerClosed`, `ReadError`, `WriteError`,
      `IdleTimeout`, `HandshakeFailed`, `BacklogLimit`, `LocalClose`, and
      `ProtocolViolation`.
- [ ] `TcpOptions.IdleTimeout` defaults to zero, which disables it. A
      positive value enables it. The key appears in the docs tables.
- [ ] The receive loop arms a deadline for each wait only when `IdleTimeout`
      is enabled. The default path allocates nothing per wait.
- [ ] On expiry, the connection closes and fires `Disconnected` once with
      `IdleTimeout`.
- [ ] `IRpcConnection.Disconnected` becomes
      `event Action<RpcDisconnectReason> Disconnected`. There is no
      `LastDisconnectReason` property.
- [ ] `IRpcConnection.Close` becomes
      `Close(RpcDisconnectReason reason = RpcDisconnectReason.LocalClose)`.
      The peer passes `ProtocolViolation` for a version mismatch.
- [ ] `TcpClientConnection` and `TcpServerConnection` forward the event and
      the `Close` parameter.
- [ ] A client-side handshake failure fires the event with `HandshakeFailed`.
      `ConnectAsync` still throws.
- [ ] Reasons are correct for peer EOF (`PeerClosed`), read failure
      (`ReadError`), write failure (`WriteError`), idle timeout, handshake
      failure, backlog limit, local close, and decoder rejection
      (`ProtocolViolation`).
- [ ] A test proves a silent peer closes after the deadline.
- [ ] A test proves a peer that sends one byte inside the deadline stays
      connected.
- [ ] A test proves the reason for EOF, local close, idle timeout, write
      failure, and protocol violation.
- [ ] No test sleeps. Use a short configured deadline and a deterministic
      signal.
- [ ] The docs pass updates the TCP options tables and `rpc-api`.
- [ ] No wire change.

## Implementation notes

- Use a linked `CancellationTokenSource` with `CancelAfter` for each wait,
  armed only when `IdleTimeout` is enabled. This matches the handshake
  pattern at
  [TcpConnection.cs:139](../../NsdRpc.Tcp/TcpConnection.cs:139). One enabled
  connection allocates one source per receive burst. The default disabled
  key keeps the default path allocation-free. If a later performance issue
  rejects that cost, replace the design with a delay race.
- The timeout measures received bytes, not sent bytes. A send-only consumer
  must send keepalives or disable the key. Document this rule.
- `Close(reason)` records the reason and fires the event with it.
  Transport-internal failures (read error, write error, idle timeout,
  backlog limit, decoder rejection) fire with their own reason without going
  through `Close`.
- Distinguish EOF (`PeerClosed`) from a socket failure (`ReadError`) in the
  receive path. Today both collapse to a false return.
- A handshake failure happens before the peer exists. The connection fires
  the event with `HandshakeFailed` before `ConnectAsync` throws.
