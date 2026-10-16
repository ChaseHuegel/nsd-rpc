# Bug: The server transport drops a frame that arrives before the peer subscribes

- Type: bug
- Status: open
- Workflow: ../specs/issues.md

## Problem

`TcpServerTransport.CompleteHandshakeAsync` calls `StartReceiving` and then
enqueues the connection
([TcpServerTransport.cs](../../NsdRpc.Tcp/TcpServerTransport.cs)). The
receive loop runs before the consumer subscribes to `FrameReceived`.

`RpcServer.AcceptLoopAsync` takes the connection from `AcceptAsync` and then
constructs the `RpcPeer`, which subscribes the frame handler
([RpcServer.cs:123](../../NsdRpc/RpcServer.cs:123)).

A frame that arrives in that window reaches no handler. The frame is dropped
without a log. A client that writes a request in the same send as its
handshake can lose the first request. The connection then stays open with a
pending call.

A test found this race. `VersionMismatchClosesTheServerConnection` gates on
`ClientConnected` before it sends. The gate hides the bug instead of testing
it.

## Acceptance criteria

- [ ] The receive loop starts only after the consumer can subscribe, or the connection buffers frames until a handler exists.
- [ ] A test writes the handshake and a request in one send. The request dispatches and the call completes.
- [ ] A late subscriber still receives every buffered frame in order.
- [ ] A frame that arrives with no subscriber and no buffer rule never drops silently.
- [ ] Existing TCP integration tests pass unchanged.
