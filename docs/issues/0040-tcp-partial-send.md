# Bug: The TCP send loop drops the remainder of a partial send

- Type: bug
- Status: open
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

- [ ] The drain path sends every byte of one queued frame before it moves to the next frame. A partial count resumes at the correct offset.
- [ ] A socket failure during a partly sent frame still stops the loop and fires the disconnected path.
- [ ] A test drives a partly completed send and proves the remaining bytes are sent. The test does not sleep. Use the internal access from `InternalsVisibleTo`.
- [ ] A test sends a frame larger than a small socket send buffer over a loopback pair and proves the receiver gets the whole frame. The issue records why the test is deterministic.
- [ ] The TCP integration tests pass unchanged.
