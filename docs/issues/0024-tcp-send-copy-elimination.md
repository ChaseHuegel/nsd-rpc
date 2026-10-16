# Improvement: Remove the extra copy on the TCP send path

- Type: improvement
- Status: done
- Workflow: ../specs/issues.md

## Problem

Each send copies the frame twice and allocates a fresh array.

1. `RpcPeer.SendEnvelope` rents a pooled buffer and serializes the envelope
   into it. The buffer goes back to the pool after the send. See
   [NsdRpc/Engine/RpcPeer.cs:741](../../NsdRpc/Engine/RpcPeer.cs:741).
2. `TcpConnection.TrySend` copies the frame into a new array with
   `frame.ToArray()`. The array goes on the send queue. See
   [NsdRpc.Tcp/TcpConnection.cs:57](../../NsdRpc.Tcp/TcpConnection.cs:57).

`TrySend` allocates the copy before it checks the queue bound. A rejected
send still pays the allocation.

`SendEnvelope` returns the pooled buffer with `clearArray: true`. The clear
touches the full rented array. A full-size frame can rent 131,072 bytes from
the pool for a 65,540-byte frame.

## Options

The selected option needs a note in this issue. The note records the reason
for each rejected option.

- Option A: the transport pools its own send buffers. `TrySend` rents a
  buffer, copies the frame into it, and returns the buffer after the socket
  send. This removes the fresh allocation. It keeps one copy.
- Option B: the peer transfers ownership of the frame buffer. An optional
  internal interface lets `TcpConnection` take the buffer and return it to
  the pool after the send. This removes the copy. The public
  `IRpcConnection.TrySend` contract keeps its copy semantics for custom
  transports.
- Option C: the peer serializes the payload directly into the frame buffer.
  Option C needs generator support. See issue 0030.

## Acceptance criteria

- [x] A design note in this issue records the selected option and the reason for each rejected option.
- [x] A send performs at most one copy from the peer buffer to the socket buffer.
- [x] A rejected send (a full queue or a closed connection) allocates nothing.
- [x] A test measures the sending-thread allocations of a warmed `TrySend` call and asserts the recorded bound.
- [x] The send queue bound tests and the TCP integration tests pass unchanged.

## Resolution

Selected option: **B**, ownership transfer.

The peer rents the frame buffer, serializes into it, and hands it to the
transport through the internal `IOwnedFrameSink` interface. `TcpConnection`
queues the buffer, sends it, and returns it to the pool. The send queue holds
a length, because a rented buffer is longer than the frame. A failed send
returns the current buffer and every queued buffer to the pool.

The public `TrySend` keeps copy semantics for custom transports. It checks
the disconnect flag and the queue bound before the copy, so a rejected send
allocates nothing.

Rejected options:

- **A**: the transport pools its own send buffers. It keeps one copy from the
  peer buffer into the transport buffer.
- **C**: the peer serializes the payload directly into the frame buffer. It
  needs generator support. It is tracked by issue 0030.
