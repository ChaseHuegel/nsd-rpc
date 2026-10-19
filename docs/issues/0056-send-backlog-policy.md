# Improvement: Reliable send queue with a backlog policy

- Type: improvement
- Status: done
- Workflow: ../specs/issues.md

## Problem

`TcpConnection` has one bounded send queue. A full queue rejects the frame
([TcpConnection.cs:78](../../NsdRpc.Tcp/TcpConnection.cs:78)). The transport
has no stated policy for a peer that cannot drain its traffic.

A consumer that wants real-time behavior is tempted to drop state. That is
unsafe on a reliable ordered transport. A dropped delta loses removals,
one-shot component updates, and staged client commands. A later frame does
not repair them. Recovery requires a full state, a baseline, or a keyframe,
and the protocol has none of those.

The transport needs a clear reliable policy. Never drop an accepted frame.
Close a peer that cannot drain its backlog.

## Acceptance criteria

- [ ] The transport has one reliable send queue. There is no send class and no
      priority lane. No accepted frame is dropped.
- [ ] `SendQueueBytes` remains a hard safety cap. A send at the cap fails.
      The caller treats the failure as a peer failure.
- [ ] `TcpOptions.SendBacklogDisconnectBytes` and
      `TcpOptions.SendBacklogDisconnectMs` exist. Both default to zero, which
      disables the disconnect. A consumer opts in. The byte threshold must
      not exceed `SendQueueBytes`. Construction fails fast when it does.
- [ ] If the queued bytes stay at or above the threshold for the window, the
      connection closes with `BacklogLimit`.
- [ ] The backlog check runs on enqueue and on drain. It uses the monotonic
      timestamp source. There is no timer per connection.
- [ ] `RpcContext.TryStream<TChunk>(in TChunk chunk)` returns false when the
      send is rejected by a full queue or a closed connection. Misuse, a
      chunk after `Respond` or after a cancellation, still asserts, like
      `Stream`. `rpc-api` states the difference.
- [ ] A test proves a transient spike that drains inside the window keeps
      the connection open.
- [ ] A test proves a sustained backlog closes the connection with
      `BacklogLimit`.
- [ ] A test proves an accepted reliable frame is never dropped.
- [ ] A test proves a rejected `TryStream` is observable to the handler.
- [ ] No test sleeps. Use a short configured window and a deterministic
      signal.
- [ ] The docs pass updates the TCP options tables in `rpc-api` and
      `quick-reference`.
- [ ] No wire change.

## Implementation notes

- The threshold must not exceed the hard cap. The cap protects memory. The
  threshold protects liveness.
- Pacing is the producer's responsibility. A large stream must pace itself or
  use `TryStream`. The library does not silently drop a chunk.
- Issue 0060 adds a batch wait to the drain. The wait must stay inside the
  remaining backlog window, so the drain-side check here stays prompt.
- A future UDP transport can add unreliable classes. That is out of scope
  here.
