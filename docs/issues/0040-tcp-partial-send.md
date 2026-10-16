# Bug: The TCP send loop drops the remainder of a partial send

- Type: bug
- Status: done
- Workflow: ../specs/issues.md

## Problem

`TcpConnection.DrainSendQueueAsync` awaits `Socket.SendAsync` and discards
the returned byte count:
[TcpConnection.cs:135](../../NsdRpc.Tcp/TcpConnection.cs:135).

A stream socket send may send fewer bytes than the buffer holds. The method
treats every send as complete and dequeues the next frame. The unsent bytes
of the frame are lost. The receiver then sees a truncated frame and drops
the connection.

The handshake path already loops until every byte is sent:
[TcpConnection.cs:185](../../NsdRpc.Tcp/TcpConnection.cs:185). The send
queue path does not.

A partial send is more likely when the send queue is large and the kernel
send buffer is nearly full.

## Acceptance criteria

- [x] The drain path sends every byte of one queued frame before it moves to the next frame. A partial count resumes at the correct offset.
- [x] A socket failure during a partly sent frame still stops the loop and fires the disconnected path.
- [x] A test drives a partly completed send and proves the remaining bytes are sent. The test does not sleep. Use the internal access from `InternalsVisibleTo`.
- [x] A test sends a frame larger than a small socket send buffer over a loopback pair and proves the receiver gets the whole frame. The issue records why the test is deterministic.
- [x] The TCP integration tests pass unchanged.

## Resolution

The drain path uses `SendAllAsync`, the same loop the handshake uses. It
resumes at the returned offset and treats a zero send as a failure. The
loopback test sets `SendBufferSize` to 4,096 and sends a 65,536-byte frame,
so the first send cannot complete in one call.
