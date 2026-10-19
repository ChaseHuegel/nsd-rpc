# Quick Reference

One subject: the public API surface of the shipped packages. This page is a
signature index. For behavior and rules, follow the links. The code is the
source of truth.

Behavior details live in [rpc-api](specs/rpc-api.md),
[rpc-protocol](specs/rpc-protocol.md), and [nsd-schemas](specs/nsd-schemas.md).
A worked example lives in [getting-started](getting-started.md).

## Packages and namespaces

| Package | Namespace | Holds |
|---|---|---|
| `NsdRpc` | `NsdRpc` | The engine, the handlers, the transport interfaces, and the wire types. |
| `NsdRpc.Tcp` | `NsdRpc.Tcp` | The TCP transport. References `NsdRpc`. |
| `NsdRpc.Generators` | none | The `[RpcService]` source generator. Reference it as an analyzer. |

## Composition roots

### RpcClient

One client endpoint: a peer over one connection. Own it for the life of the
connection.

| Member | Purpose |
|---|---|
| `RpcClient(IRpcConnection connection, RpcRegistry registry, RpcOptions? options = null)` | Builds the client. |
| `InvokeAsync<TRequest, TResponse>(ulong method, in TRequest request, TimeSpan? timeout = null, CancellationToken cancellationToken = default)` | Calls one method. |
| `InvokeStreamAsync<TRequest, TChunk, TResponse>(ulong method, in TRequest request, IChunkSink<TChunk> sink, ...)` | Calls one method and routes each chunk to a sink. |
| `InvokeStreamAsync<TRequest, TChunk, TResponse>(ulong method, in TRequest request, ...)` | Calls one method and returns an `RpcStreamSession<TChunk, TResponse>`. |
| `Notify<TRequest>(ulong method, in TRequest request)` | Sends a notification. |
| `Notify(ulong method)` | Sends a notification with no payload. |
| `HandleFrame(ReadOnlySpan<byte> frame)` | Feeds one frame to the peer. Pull-based transports use it. |
| `CheckTimeouts(long timestampNanos)` | Enforces the call deadlines. |
| `Poll()` | Checks the deadlines with the current time. |
| `Peers` | The single peer. |

### RpcServer

One server endpoint: one peer per accepted connection.

| Member | Purpose |
|---|---|
| `RpcServer(IRpcServerTransport transport, RpcRegistry registry, RpcOptions? options = null)` | Builds the server. |
| `Start()` | Launches the accept loop. Call once. |
| `Close()` | Stops accepting and faults every pending call. |
| `GetPeer(ulong clientId)` | Returns the peer for one client, or null. |
| `Peers` | The live peers as a `PeerSet`. A snapshot. |
| `ClientConnected` / `ClientDisconnected` | Events. Fire when a peer is wired or leaves. |
| `CheckTimeouts(long timestampNanos)` / `Poll()` | Enforce the deadlines for every peer. |

`RpcServer` is not a caller. Select a peer from `Peers` or `GetPeer` and call
it. To reach every peer, loop the set. See [rpc-api](specs/rpc-api.md).

### RpcPeer

The engine for one connection. `RpcClient` and `RpcServer` expose it.

| Member | Purpose |
|---|---|
| `RpcPeer(IRpcConnection connection, RpcOptions? options = null)` | Builds the peer. |
| `ClientId` | The server-assigned id. Zero on the client side. |
| `Peers` | The peer itself. |
| `InvokeAsync` / `InvokeStreamAsync` / `Notify` | The call surface. Same shape as `RpcClient`. |
| `HandleFrame(ReadOnlySpan<byte> frame)` | Feeds one frame. |
| `CheckTimeouts(long timestampNanos)` / `Poll()` | Enforce the deadlines. |
| `FaultPending(int errorCode, string message)` | Completes every pending call with the given error. |
| `Stats` | The connection counters, or zeros when the transport has none. |

### IRpcCaller and IRpcHost

| Type | Purpose |
|---|---|
| `IRpcCaller` | The call surface (`InvokeAsync`, `InvokeStreamAsync`, `Notify`) implemented by `RpcClient` and `RpcPeer`. |
| `IRpcHost` | Exposes `PeerSet Peers`. `RpcServer` exposes the live peers. |
| `PeerSet` | An immutable peer snapshot. `foreach` does not allocate. |

## Calls and results

### RpcResult\<T\>

| Member | Purpose |
|---|---|
| `IsSuccess` | True when the call succeeded. |
| `Value` | The result value. The default value on failure. |
| `ErrorCode` | The error code. Zero on success. Cast it to `RpcErrorCode`. |
| `ErrorMessage` | The error text. Empty on success. |
| `Success(T value)` | Builds a success. |
| `Failure(int errorCode, string errorMessage)` | Builds a failure. The code must be nonzero. |

### RpcContext

The handle for one inbound message. A deferred handler keeps it.

| Member | Purpose |
|---|---|
| `Connection` | The connection the message arrived on. |
| `ClientId` | The server-assigned id. Zero on the client side. |
| `Host` | The endpoint that received the message. Fan out through `Host.Peers`. |
| `Method` | The method id of the inbound message. |
| `RequestId` | The request id. Zero for a notification. |
| `IsServer` | True when the handler runs on the server side. |
| `Stream<TChunk>(in TChunk chunk)` | Sends one chunk before the response. |
| `TryStream<TChunk>(in TChunk chunk)` | Sends one chunk. Returns false when the send is rejected. |
| `Respond<TResponse>(in RpcResult<TResponse> result)` | Sends the response. |
| `Fail(RpcErrorCode code, string message)` | Sends a failure response. |

### RpcStreamSession\<TChunk, TResponse\>

| Member | Purpose |
|---|---|
| `Chunks` | The chunks as an `IAsyncEnumerable<TChunk>`. |
| `Response` | The final response. Completes after the last chunk. |

A full chunk queue faults the call with `Busy`. The bound is
`RpcOptions.MaxBufferedChunks`.

### IChunkSink\<TChunk\>

| Member | Purpose |
|---|---|
| `OnChunk(in TChunk chunk)` | Consumes one chunk. Runs on the receive pump. Return quickly. |

### RpcErrorCode

| Name | Value | Meaning |
|---|---|---|
| `InvalidRequest` | -32600 | The envelope is malformed for its kind. |
| `MethodNotFound` | -32601 | No handler is registered, or the direction is wrong. |
| `InvalidParams` | -32602 | The payload did not decode. |
| `InternalError` | -32603 | The handler threw. The wire carries no detail. |
| `RequestTimeout` | -32001 | The call passed its deadline. Local only. |
| `PermissionDenied` | -32002 | Reserved. |
| `Unavailable` | -32003 | The connection is down or gone. Local only. |
| `Busy` | -32004 | The receiver is at capacity, or a stream queue is full. |
| `Cancelled` | -32005 | The caller's token fired. Local only. |

## Handlers and dispatch

### RpcRegistry

Build it at startup, then freeze it.

| Member | Purpose |
|---|---|
| `Add<TRequest, TResponse>(IRpcHandler<TRequest, TResponse> handler)` | Adds a handler. Reads the id from `[RpcMethod]`. |
| `Add<TRequest, TResponse>(IRpcHandler<TRequest, TResponse> handler, ulong methodId, RpcDirection direction = RpcDirection.Both)` | Adds a handler with an explicit id. Generated bases use it. |
| `AddService(IRpcServiceRegistrar service)` | Adds every handler of a generated service base. |
| `ScanAssembly(Assembly assembly)` | Adds every marked handler class with a parameterless constructor. |
| `Freeze()` | Locks the registry. A later `Add` throws. |

A missing attribute, a zero id, or a duplicate id fails fast.

### RpcMethodAttribute

| Member | Purpose |
|---|---|
| `RpcMethodAttribute(ulong method, RpcDirection direction = RpcDirection.Both)` | Marks a handler class or a service method. |
| `Method` | The method id. Zero is never valid. |
| `Direction` | Which side may handle the method. |
| `Streams` | The chunk type for a server handler. Null for a non-streaming method. |

The method id is a wire contract. Never change it between builds.

### RpcDirection

| Name | Meaning |
|---|---|
| `Both` | Both peers may handle the method. |
| `ClientToServer` | Only the server side may handle the method. |
| `ServerToClient` | Only the client side may handle the method. |

A call in the wrong direction answers `MethodNotFound`.

### IRpcHandler\<TRequest, TResponse\>

| Member | Purpose |
|---|---|
| `ExecuteAsync(TRequest request, RpcContext context, CancellationToken cancellationToken)` | Handles one message. Returns a `ValueTask<RpcResult<TResponse>>`. |

One interface serves sync and async handlers. Return a completed `ValueTask`
from a sync handler.

### IRpcServiceRegistrar

| Member | Purpose |
|---|---|
| `RegisterHandlers(RpcRegistry registry)` | Registers every handler of a service. Generated bases implement it. |

## Configuration

### RpcOptions

Read at peer construction. Mutating it later is not supported.

| Key | Default | Meaning |
|---|---|---|
| `Timeout` | 30 seconds | The default call timeout. Zero disables the deadline. |
| `MaxFrameBytes` | 65570 | The largest frame, including the 4-byte length prefix. |
| `MaxPendingCalls` | 1024 | The most pending calls and inbound requests one peer tracks. |
| `MaxBufferedChunks` | 1024 | The most chunks one stream session buffers. |
| `TimestampNanos` | monotonic clock | A nanosecond timestamp source. |

### TcpOptions

Read at transport construction.

| Key | Default | Meaning |
|---|---|---|
| `Host` | `"localhost"` | The remote host for the client. The server ignores it. |
| `Port` | 0 | The port. Zero binds an ephemeral port on the server. |
| `BindAddress` | `IPAddress.Any` | The local address the listener binds. Null fails fast. |
| `ConnectTimeout` | 10 seconds | The deadline for one connect attempt. Zero or negative disables it. |
| `NoDelay` | `true` | Disables Nagle's algorithm. |
| `HandshakeTimeout` | 10 seconds | The deadline for one handshake. Zero or negative disables it. |
| `IdleTimeout` | `0` (disabled) | Closes the connection when no bytes arrive for this period. Zero or negative disables it. |
| `ReceiveBufferBytes` | 65536 | The receive buffer size per read. |
| `DecoderIdleShrinkAfter` | 30 seconds | The decoder shrinks after this idle period. Zero or negative disables it. |
| `SendQueueBytes` | 1048576 | The largest queued send bytes per connection. A full queue rejects sends. |
| `SendBacklogDisconnectBytes` | `0` (disabled) | The queued send bytes that start the backlog window. Must not exceed `SendQueueBytes`. |
| `SendBacklogDisconnectMs` | `0` (disabled) | The window for the backlog disconnect. The two backlog keys must both be zero or both be positive. |

## TCP transport

| Type | Members | Purpose |
|---|---|---|
| `TcpClientConnection` | `TcpClientConnection(RpcOptions, TcpOptions)`, `ConnectAsync()`, `IsConnected`, `Close()` | A client connection. Connect it, then pass it to `RpcClient`. |
| `TcpServerTransport` | `TcpServerTransport(RpcOptions, TcpOptions)`, `Port`, `AcceptAsync()`, `Close()` | A listener. Pass it to `RpcServer`. `Port` is the bound port. |

## Transports

Implement these interfaces to add a transport. `NsdRpc.Tcp` is the built-in
one.

| Type | Purpose |
|---|---|
| `IRpcConnection` | `IsConnected`, `Close(reason)`, `TrySend(ReadOnlySpan<byte>)`, `Disconnected`, `FrameReceived`. |
| `IClientConnection : IRpcConnection` | Adds `ConnectAsync()`. |
| `IServerConnection : IRpcConnection` | Adds `ClientId` and `RemoteEndPoint`. |
| `IRpcServerTransport` | `AcceptAsync()` and `Close()`. |
| `RpcDisconnectReason` | The reason carried by the `Disconnected` event. See [rpc-api](specs/rpc-api.md). |
| `IRpcConnectionStats` | Optional capability: `RpcConnectionStats Stats`. |
| `RpcConnectionStats` | Frames, bytes, queue depth, writes, and refused sends. |

The peer owns the 4-byte length prefix. A custom transport implements its own
framing. The contract lives in [rpc-api](specs/rpc-api.md).

## Service generator

Reference `NsdRpc.Generators` as an analyzer and mark an interface with
`[RpcService]`. The generator emits two types in the interface namespace.

| Shape | Role | Return type | Generated member |
|---|---|---|---|
| No `RpcContext` | Invocation | `Task<RpcResult<T>>` | A plain call on `{Name}Client`. |
| No `RpcContext` | Invocation | `RpcStreamSession<TChunk, T>` or `Task<RpcResult<T>>` with `IChunkSink<TChunk>` | A stream call on `{Name}Client`. |
| No `RpcContext` | Invocation | `void` | A notification on `{Name}Client`. |
| With `RpcContext` | Handler | `ValueTask<RpcResult<T>>` | A virtual member on `{Name}Handler`. |
| With `RpcContext` | Handler | `void` | A virtual notification member on `{Name}Handler`. |

- `{Name}` is the interface name without a leading `I`. `IChat` generates
  `ChatClient` and `ChatHandler`.
- `{Name}Client` has two constructors: `(RpcClient client)` and
  `(RpcPeer peer)`. On the base, a `void` handler member returns
  `ValueTask<RpcResult<RpcUnit>>`. `RpcUnit` is a generated placeholder.
- An unhandled call on the base answers `InternalError`. An unhandled
  notification does nothing.
- Register a concrete `{Name}Handler` subclass with `RpcRegistry.AddService`.
- A mixed interface is a declaration for the generator. A single-role
  interface is also implemented by the generated client.

The full rules live in [rpc-api](specs/rpc-api.md).
