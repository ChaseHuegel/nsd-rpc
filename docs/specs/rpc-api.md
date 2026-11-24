# RPC Engine and Handler API

One subject: the API of the RPC library. Read
[rpc-protocol](rpc-protocol.md) for the wire contract. The implementation is
complete.

## Layers

| Layer | Types | Role |
| --- | --- | --- |
| 0 | `IRpcConnection`, `IClientConnection`, `IServerConnection`, `IRpcServerTransport`, `RpcOptions` | transport and config |
| 1 | `RpcPeer`, `RpcContext`, `RpcResult<T>`, `RpcErrorCode` | envelope, correlation, timeouts |
| 2 | `RpcMethodAttribute`, `RpcRegistry`, `IRpcHandler`, `IPayloadSerializer` | handlers and dispatch |

`RpcClient` and `RpcServer` compose a peer, a transport, and a registry.

## Layer 0

The interfaces live in
[NsdRpc/Transport/](../../NsdRpc/Transport/IRpcConnection.cs). Options live
in [NsdRpc/RpcOptions.cs](../../NsdRpc/RpcOptions.cs).

```csharp
public interface IRpcConnection
{
    bool IsConnected { get; }
    void Close(RpcDisconnectReason reason = RpcDisconnectReason.LocalClose);
    bool TrySend(ReadOnlySpan<byte> frame);
    event Action<RpcDisconnectReason> Disconnected;
    event RpcFrameHandler FrameReceived;
}

public interface IClientConnection : IRpcConnection
{
    Task ConnectAsync();
}

public interface IServerConnection : IRpcConnection
{
    ulong ClientId { get; }
    EndPoint? RemoteEndPoint { get; }
}

public interface IRpcServerTransport
{
    Task<IServerConnection?> AcceptAsync();
    void Close();
}
```

Rules:

- `Close` ends the connection and fires `Disconnected` once with the reason.
  The reason defaults to `LocalClose`. The peer passes `ProtocolViolation`
  when a frame carries another protocol version.
- `TrySend` copies the frame before it returns. It returns false when the
  frame was rejected: a closed connection or a full queue.
- `FrameReceived` fires for each complete frame from the transport's receive
  loop. The span is only valid during the call. Pull-based transports never
  fire it.
- A frame is a 4-byte little-endian length prefix and the envelope bytes. The
  peer builds the prefix on sends and validates it on receives. The
  `NsdRpc.Tcp` decoder enforces the bound while accumulating. Custom
  transports implement their own framing.
- `ClientId` comes from the server transport at accept. It is never 0. It is
  0 on the client side.

### Disconnect reasons

`RpcDisconnectReason` travels on the `Disconnected` event. It is not wire
data.

| Value | Meaning |
|---|---|
| `PeerClosed` | The peer closed its send side. The read returned zero bytes. |
| `ReadError` | A socket read failed. |
| `WriteError` | A socket write failed. |
| `IdleTimeout` | No bytes arrived within `TcpOptions.IdleTimeout`. |
| `HandshakeFailed` | The handshake failed or passed its deadline. |
| `BacklogLimit` | The outbound backlog stayed above its threshold for too long, or the early receive queue passed `ReceiveQueueBytes`. |
| `LocalClose` | The local host called `Close`. |
| `ProtocolViolation` | The peer broke the framing or protocol rules. |

### Connection stats

`IRpcConnectionStats` is an optional transport capability. `TcpConnection` and
both TCP connection wrappers implement it. `RpcPeer.Stats` reads it and
returns zeros when the transport has none.

| Field | Meaning |
|---|---|
| `PacketsSent` | The frames sent. |
| `PacketsReceived` | The frames received. |
| `BytesSent` | The bytes sent, including the 4-byte length prefix. |
| `BytesReceived` | The bytes received, including the 4-byte length prefix. |
| `QueuedBytes` | The current outbound queue size. |
| `PeakQueuedBytes` | The high-water mark since construction. |
| `Writes` | The socket writes. One frame may take several writes. |
| `RejectedFrames` | The refused sends. |

`Packets` counts frames. `Writes` counts socket writes. The counters exclude
the handshake. A custom transport implements the capability to report its own
values.

## RpcOptions

```csharp
public sealed class RpcOptions
{
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMilliseconds(30000); // 0 disables
    public int MaxFrameBytes { get; set; } = 65570; // the largest valid frame
    public int MaxPendingCalls { get; set; } = 1024;
    public int MaxBufferedChunks { get; set; } = 1024;
    public Func<long> TimestampNanos { get; set; } = ...; // monotonic
    public Action<TraceEventType, string, Exception?>? LogSink { get; set; } = null;
}
```

- Every limit is a config key. No magic constants exist in code.
- A null options argument uses the private default instance.
- The peer reads options at construction. Changes after construction are not
  supported.
- Tests inject a fake `TimestampNanos`. No test sleeps.

## TcpOptions

The TCP transport options live in
[NsdRpc.Tcp/TcpOptions.cs](../../NsdRpc.Tcp/TcpOptions.cs).

| Key | Default | Meaning |
|---|---|---|
| `Host` | `"localhost"` | The remote host for the client. The server ignores it. |
| `Port` | `0` | The port. Zero binds an ephemeral port on the server. |
| `BindAddress` | `IPAddress.Any` | The local address the listener binds. Null fails fast. |
| `ConnectTimeout` | 10 seconds | The deadline for one connect attempt. Zero or negative disables it. |
| `NoDelay` | `true` | Disables Nagle's algorithm. |
| `HandshakeTimeout` | 10 seconds | The deadline for one handshake. Zero or negative disables it. |
| `IdleTimeout` | `0` (disabled) | Closes the connection when no bytes arrive for this period. Zero or negative disables it. |
| `ReceiveBufferBytes` | 65,536 | The receive buffer size per read. The receive loop rents it per read. |
| `ReceiveQueueBytes` | 1,048,576 | The largest buffered receive bytes before a `FrameReceived` subscriber attaches. Must be positive. |
| `DecoderIdleShrinkAfter` | 30 seconds | The decoder shrinks to its start size after this idle period. Zero or negative disables it. |
| `SendQueueBytes` | 1,048,576 | The largest queued send bytes for one connection. A full queue rejects the send. |
| `SendBacklogDisconnectBytes` | `0` (disabled) | The queued send bytes that start the backlog window. Must not exceed `SendQueueBytes`. |
| `SendBacklogDisconnectMs` | `0` (disabled) | The window for the backlog disconnect. The two backlog keys must both be zero or both be positive. |
| `SendBatchInterval` | `0` (disabled) | Coalesces queued frames for this interval into one write. Zero sends immediately. |
| `SendBatchMaxBytes` | 65,536 | The largest bytes in one batched write. A larger frame is written alone. |

The send backlog disconnect is opt-in. When the queued send bytes stay at or
above `SendBacklogDisconnectBytes` for `SendBacklogDisconnectMs`, the
connection closes with `BacklogLimit`. The check runs on enqueue and on
drain. It uses the monotonic timestamp source and no timer.

Send batching is opt-in. With a positive `SendBatchInterval`, the drain waits
the interval for more frames, then writes up to `SendBatchMaxBytes` in one
call. A frame larger than the limit is written alone. A full batch wakes the
wait early. Zero keeps one write per frame.

The server transport completes each handshake in the background. A silent
client does not stall the other clients. A handshake that passes its
deadline closes the connection. `ConnectAsync` throws `TimeoutException` when
the connect passes `ConnectTimeout`. The listener binds `BindAddress`.

The idle timeout measures received bytes, not sent bytes. A send-only
consumer must send keepalives or disable the key. A silent peer closes with
`IdleTimeout`.

The connection buffers any frame that arrives before `FrameReceived` has a
subscriber, in arrival order, up to `ReceiveQueueBytes`. The first subscriber
drains every buffered frame before later frames arrive. Exceeding
`ReceiveQueueBytes` closes the connection with `BacklogLimit`.

## RpcPeer

The engine lives in
[NsdRpc/Engine/](../../NsdRpc/Engine/RpcPeer.cs).

```csharp
public sealed class RpcPeer
{
    public Task<RpcResult<TResponse>> InvokeAsync<TRequest, TResponse>(
        ulong method, in TRequest request, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    public Task<RpcResult<TResponse>> InvokeStreamAsync<TRequest, TChunk, TResponse>(
        ulong method, in TRequest request, IChunkSink<TChunk> sink,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default);

    public RpcStreamSession<TChunk, TResponse> InvokeStreamAsync<TRequest, TChunk, TResponse>(
        ulong method, in TRequest request, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    public bool Notify<TRequest>(ulong method, in TRequest request);
    public bool Notify(ulong method);

    public void HandleFrame(ReadOnlySpan<byte> frame);
    public void CheckTimeouts(long timestampNanos);
    public void FaultPending(int errorCode, string message);
    public void Poll();
    public void Close();
    public EndPoint? RemoteEndPoint { get; }
    public RpcConnectionStats Stats { get; }
}
```

`RpcClient` exposes the same call surface. It forwards to its peer.

Engine rules:

- Ids start at 1. The counter uses checked arithmetic. Overflow faults the
  connection. Ids are never reused.
- `MaxPendingCalls` bounds the pending table, the inbound set, and the
  dispatch queue. A full table fails a new call with `Busy` before it sends.
  A full inbound set answers a new request with `Busy`. A full dispatch queue
  drops a notification and logs the drop.
- One pump per peer processes frames in arrival order. Handler calls run in
  order.
- Network input is never asserted. Unknown ids, duplicate responses, and
  late chunks are dropped and logged.
- Handler misuse asserts. Streaming after `Respond`, or `Respond` twice, is a
  programmer error.
- A token firing sends a cancellation frame, removes the entry, and completes
  the call with `Cancelled`.
- A deadline sends a cancellation frame, removes the entry, and completes the
  call with `RequestTimeout`.
- A failed send completes the call with `Unavailable`.
- `FaultPending` completes every pending call with the given error. It runs
  on disconnect and cleanup.
- The transport raises `FrameReceived` from its receive loop. The peer
  subscribes at construction. The transport or the host calls
  `CheckTimeouts` with a timestamp from `RpcOptions.TimestampNanos`. `Poll`
  is the convenience form: it checks deadlines with the current timestamp. A
  pull-based transport drains its queue into `HandleFrame` first.
- `RemoteEndPoint` is the client endpoint on the server side. It is null on
  the client side. `Close` closes the connection and fires `LocalClose`. The
  raw connection stays internal.
- `LogSink` receives drop and error events. A null sink keeps the TraceSource
  default. The sink is not gated by the TraceSource switch. A sink that
  throws is contained.

## RpcClient and RpcServer

The composition roots live in
[NsdRpc/RpcClient.cs](../../NsdRpc/RpcClient.cs) and
[NsdRpc/RpcServer.cs](../../NsdRpc/RpcServer.cs).

```csharp
public sealed class RpcClient : IRpcHost, IRpcCaller
{
    public RpcClient(IRpcConnection connection, RpcRegistry registry, RpcOptions? options = null);
    // InvokeAsync, InvokeStreamAsync, Notify: the call surface of its one peer.
    public void HandleFrame(ReadOnlySpan<byte> frame);
    public void CheckTimeouts(long timestampNanos);
    public void Poll();
}

public sealed class RpcServer : IRpcHost
{
    public RpcServer(IRpcServerTransport transport, RpcRegistry registry, RpcOptions? options = null);
    public void Start();   // launches the accept loop. Call once.
    public RpcPeer? GetPeer(ulong clientId);
    public PeerSet Peers { get; }
    public void CheckTimeouts(long timestampNanos);
    public void Poll();
    public void Close();
    public event Action<RpcPeer> ClientConnected;
    public event Action<RpcPeer> ClientDisconnected;
}
```

- `RpcClient` owns one peer over one connection. It is a singleton in
  practice because it owns the id space.
- `RpcServer` owns one peer per connection, keyed by `ClientId`. It accepts
  through the transport. It prunes dead peers and faults their calls.
- `RpcServer` is not a caller. It exposes its peers. A caller selects a peer
  from `Peers` or `GetPeer` and calls it. There is no broadcast.
- `GetPeer` returns null for an unknown client id.
- `ClientConnected` fires after the accept loop wires a peer.
  `ClientDisconnected` fires when a peer leaves, through a disconnect, a
  prune, or a close.
- Both types are plain constructible classes. The app owns their lifetime.

## IRpcHost

`IRpcHost` exposes `PeerSet Peers`. `RpcPeer` and `RpcClient` expose one peer.
`RpcServer` exposes an immutable snapshot of its live peers.

`PeerSet` is a readonly value over an immutable array. Iterating it does not
allocate. A default `PeerSet` is empty. A captured snapshot does not change
when the live peers change.

## Fan-out from a handler

A handler reaches the peers through its context. `RpcContext.Host` is the
endpoint that received the message. On the server it is the `RpcServer`, so
`context.Host.Peers` is every connected client. Loop it to reach every peer:

```csharp
foreach (RpcPeer peer in context.Host.Peers)
{
    peer.Notify(chatMethod, in message);
}
```

The handler holds no peer set, so one instance serves any server. To answer
the caller alone, call `Respond` or `Stream` on the context.

## RpcContext

```csharp
public readonly struct RpcContext
{
    public IRpcConnection Connection { get; }
    public ulong ClientId { get; }
    public IRpcHost Host { get; }
    public ulong Method { get; }
    public ulong RequestId { get; }
    public bool IsServer { get; }

    public void Stream<TChunk>(in TChunk chunk);
    public bool TryStream<TChunk>(in TChunk chunk);
    public void Respond<TResponse>(in RpcResult<TResponse> result);
    public void Fail(RpcErrorCode code, string message);
}
```

- A deferred handler keeps the context and calls `Respond` later.
- `Respond` and `Stream` are safe from any thread. Sends are queue-based.
- `Stream` sends chunks before the response. The peer drops them after a
  cancellation or a response.
- `TryStream` sends one chunk and returns false when the send is rejected.
  `Stream` keeps the assert behavior and drops a rejected chunk with a log.
  A producer that paces a large stream uses `TryStream`.
- `ClientId` is 0 on the client side. `IsServer` tells the handler which side
  it runs on.
- `Host` is the endpoint that received the message. A handler fans out
  through `Host.Peers`.

## Handlers

The handler types live in
[NsdRpc/Handlers/](../../NsdRpc/Handlers/RpcMethodAttribute.cs).

```csharp
[AttributeUsage(AttributeTargets.Class)]
public sealed class RpcMethodAttribute : Attribute
{
    public ulong Method { get; }
    public RpcDirection Direction { get; }

    public RpcMethodAttribute(ulong method, RpcDirection direction = RpcDirection.Both) ...
}

public interface IRpcHandler<TRequest, TResponse>
{
    ValueTask<RpcResult<TResponse>> ExecuteAsync(
        TRequest request, RpcContext context, CancellationToken cancellationToken);
}

public readonly struct RpcResult<T>
{
    public bool IsSuccess { get; }
    public T Value { get; }
    public int ErrorCode { get; }
    public string ErrorMessage { get; }

    public static RpcResult<T> Success(T value);
    public static RpcResult<T> Failure(int errorCode, string errorMessage);
}
```

- One interface serves sync and async handlers. A sync handler returns a
  completed ValueTask.
- The parameters pass by value. The C# language forbids `in` parameters on
  async methods, so the interface must stay by-value to allow async handlers.
- Expected failures are results. Exceptions are programmer errors.
- The attribute is the single source of truth for the method id. Manual
  registration reads it from the handler type.
- `RpcDirection` is `Both`, `ClientToServer`, or `ServerToClient`. A call in
  the wrong direction gets `MethodNotFound`.
- The token fires when the peer receives a cancellation or the connection
  dies.

## Registry

```csharp
public sealed class RpcRegistry
{
    public void Add<TRequest, TResponse>(IRpcHandler<TRequest, TResponse> handler);
    public void Add<TRequest, TResponse>(IRpcHandler<TRequest, TResponse> handler, ulong methodId, RpcDirection direction = RpcDirection.Both);
    public void AddService(IRpcServiceRegistrar service);
    public void ScanAssembly(Assembly assembly);
    public void Freeze();
}
```

- `Add` reads the id from the attribute. The id-explicit overload takes the id
  and direction directly and is what generated service bases use.
- `AddService` calls the service's `RegisterHandlers`. Generated handler bases
  implement `IRpcServiceRegistrar`.
- A missing attribute, a zero id, or a duplicate id fails fast.
- `ScanAssembly` instantiates marked classes that implement the handler
  interface with a parameterless constructor. A marked but non-conforming
  class fails fast.
- The registry is built at startup. `Freeze` locks it. A later `Add` throws.

## Dispatch

- A direction mismatch answers `MethodNotFound`.
- A payload that does not decode answers `InvalidParams`.
- A notification payload that does not decode is dropped and logged.
- A chunk payload that does not decode fails the call with `InvalidRequest`.
  The connection stays open.
- A handler exception answers `InternalError`. The detail is logged locally
  only.
- A handler failure outside the app range is rejected. See the code space in
  [rpc-protocol](rpc-protocol.md).
- One try/catch sits at the dispatch boundary.
- Dispatch is serial per peer.

## Payload serialization

```csharp
public interface IPayloadSerializer<T>
{
    int GetSize(in T value);
    void Serialize(in T value, ref byte[] buffer);
    void Serialize(in T value, ref byte[] buffer, int size); // size comes from GetSize
    bool TryDeserialize(ReadOnlySpan<byte> data, out T value);
}
```

- The default implementation wraps the generated nsd methods. A per-type
  cache holds one serializer per payload type.
- `TryDeserialize` returns a result. It never throws. Any reader failure is a
  decode failure.

## Streaming consumption

The wrapper lives in
[NsdRpc/Engine/RpcStreamSession.cs](../../NsdRpc/Engine/RpcStreamSession.cs).

- Callback core: `InvokeStreamAsync` with an `IChunkSink<TChunk>`. The sink
  owns the buffering policy.
- Wrapper: `RpcStreamSession` gives `Chunks` as `IAsyncEnumerable<TChunk>`
  and `Response` as a `Task`. The queue is bounded by `MaxBufferedChunks`. A
  full queue faults the stream with `Busy`.
- Cancelling the token cancels both halves.

## Typed service clients

A source generator in the
[NsdRpc.Generators](../../NsdRpc.Generators/NsdRpc.Generators.csproj) project
turns a `[RpcService]` interface into two types. Reference the generator as
an analyzer and mark the interface. The shape of each method decides its
role: an invocation shape has no `RpcContext`, a handler shape has one.

```csharp
[RpcService]
public interface IChat
{
    // Invocation shapes become invokable members on ChatClient.
    [RpcMethod(Methods.ListUsers)]
    RpcStreamSession<User, ListUsersResponse> ListUsersAsync(
        ListUsersRequest request, CancellationToken cancellationToken = default);

    [RpcMethod(Methods.ChatMessage)]
    void NotifyChat(ChatMessage message);

    // Handler shapes become abstracts on ChatHandler.
    [RpcMethod(Methods.ListUsers, Streams = typeof(User))]
    ValueTask<RpcResult<ListUsersResponse>> ListUsersAsync(
        ListUsersRequest request, RpcContext context, CancellationToken cancellationToken);

    [RpcMethod(Methods.ChatMessage)]
    void OnNotifyChat(ChatMessage message, RpcContext context, CancellationToken cancellationToken);
}
```

`ChatClient` is the invokable proxy. `ChatHandler` is one base implementing
the handler shapes, `IRpcServiceRegistrar`, and one `IRpcHandler` per
method. Its handler members are `virtual`, so a partial implementer
overrides only what it handles: an unhandled call answers `InternalError`,
an unhandled notification is a no-op. A duplex method is an invocation and
a handler sharing one id. Register a concrete subclass with `AddService`:

```csharp
public sealed class MyChatHandler : ChatHandler
{
    public override async ValueTask<RpcResult<ListUsersResponse>> ListUsersAsync(
        ListUsersRequest request, RpcContext context, CancellationToken cancellationToken)
    {
        foreach (User user in FakeUsers.All)
        {
            await Task.Delay(250, cancellationToken);
            context.Stream(user);
        }

        return RpcResult<ListUsersResponse>.Success(new ListUsersResponse(FakeUsers.All.Count));
    }

    public override void OnNotifyChat(ChatMessage message, RpcContext context, CancellationToken cancellationToken)
    {
        Console.WriteLine(message.Author + ": " + message.Text);
    }
}

registry.AddService(new MyChatHandler());
```

The client reads like an ordinary instance method, with a second constructor
taking an `RpcPeer` for server-to-client calls:

```csharp
var chat = new ChatClient(client);
RpcResult<ListUsersResponse> result = await chat.ListUsersAsync(new ListUsersRequest(0));
```

Invocation shapes are the same as before: `Task<RpcResult<T>>` is a plain
call, `RpcStreamSession<TChunk, T>` is a stream, the `IChunkSink<TChunk>`
overload is the callback stream, and `void` or `bool` is a notification. A
`bool` notification member returns the result of `IRpcCaller.Notify`: `true`
when the peer accepts the frame and `false` when it rejects the frame. A handler
shape is `ValueTask<RpcResult<T>>` for a call and `void` for a notification. A
notification handler cannot return `bool`, because it runs locally and has no
frame to reject.

The handler's `Streams` declares the chunk type; the generator cross-checks
it against the client's chunk type when both shapes share an id. A generated
type implements the interface only when the interface is single-role; a
mixed interface is a declaration consumed by the generator, not implemented
by either type. `RpcDirection` stays optional, `Both` by default, and only
affects dispatch enforcement. See
[issue 0018](../issues/0018-shape-based-generation.md).

