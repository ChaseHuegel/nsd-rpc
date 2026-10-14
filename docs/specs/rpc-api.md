# RPC Engine and Handler API

One subject: the API of the RPC library. Read
[rpc-protocol](rpc-protocol.md) for the wire contract. Layer 0 and the
`RpcPeer` engine landed. Issues
[0005](../issues/0005-rpc-handler-dispatch.md) through
[0008](../issues/0008-rpc-streaming-cancellation.md) build the rest.

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
    bool TrySend(ReadOnlySpan<byte> frame);
    event Action Disconnected;
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

- `TrySend` copies the frame before it returns. It returns false when the
  frame was rejected: a closed connection or a full queue.
- `Disconnected` fires once. The peer faults every pending call.
- A frame is a 4-byte little-endian length prefix and the envelope bytes. The
  frame codec lives in the `NsdRpc.Tcp` project. Custom transports implement
  their own framing.
- `ClientId` comes from the server transport at accept. It is never 0. It is
  0 on the client side.

## RpcOptions

```csharp
public sealed class RpcOptions
{
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMilliseconds(30000); // 0 disables
    public int MaxFrameBytes { get; set; } = 65536;
    public int MaxPendingCalls { get; set; } = 1024;
    public int MaxBufferedChunks { get; set; } = 1024;
    public Func<long> TimestampNanos { get; set; } = ...; // monotonic
}
```

- Every limit is a config key. No magic constants exist in code.
- A null options argument uses the private default instance.
- The peer reads options at construction. Changes after construction are not
  supported.
- Tests inject a fake `TimestampNanos`. No test sleeps.

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
}
```

`RpcClient` exposes the same call surface. It forwards to its peer.

Engine rules:

- Ids start at 1. The counter uses checked arithmetic. Overflow faults the
  connection. Ids are never reused.
- `MaxPendingCalls` bounds the pending table and the inbound set. A full
  table fails a new call with `Busy` before it sends. A full inbound set
  answers a new request with `Busy`.
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
- The transport calls `HandleFrame` from its receive loop. The transport or
  the host calls `CheckTimeouts` with a timestamp from
  `RpcOptions.TimestampNanos`. `Poll` is the convenience form: it checks
  deadlines with the current timestamp. A pull-based transport drains its
  queue into `HandleFrame` first.

## RpcClient and RpcServer

```csharp
public sealed class RpcClient
{
    public RpcClient(IRpcConnection connection, RpcRegistry registry, RpcOptions? options = null);
    // InvokeAsync, InvokeStreamAsync, Notify: same surface as RpcPeer.
}

public sealed class RpcServer
{
    public RpcServer(IRpcServerTransport transport, RpcRegistry registry, RpcOptions? options = null);
    public RpcPeer? GetPeer(ulong clientId);
    public void Close();
}
```

- `RpcClient` owns one peer over one connection. It is a singleton in
  practice because it owns the id space.
- `RpcServer` owns one peer per connection, keyed by `ClientId`. It accepts
  through the transport. It prunes dead peers and faults their calls.
- `GetPeer` returns null for an unknown client id.
- Both types are plain constructible classes. The app owns their lifetime.

## RpcContext

```csharp
public readonly struct RpcContext
{
    public IRpcConnection Connection { get; }
    public ulong ClientId { get; }
    public ulong Method { get; }
    public ulong RequestId { get; }
    public bool IsServer { get; }

    public void Stream<TChunk>(in TChunk chunk);
    public void Respond<TResponse>(in RpcResult<TResponse> result);
    public void Fail(RpcErrorCode code, string message);
}
```

- A deferred handler keeps the context and calls `Respond` later.
- `Respond` and `Stream` are safe from any thread. Sends are queue-based.
- `Stream` sends chunks before the response. The peer drops them after a
  cancellation or a response.
- `ClientId` is 0 on the client side. `IsServer` tells the handler which side
  it runs on.

## Handlers

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
        in TRequest request, in RpcContext context, CancellationToken cancellationToken);
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
    public void ScanAssembly(Assembly assembly);
    public void Freeze();
}
```

- `Add` reads the id from the attribute. A missing attribute, a zero id, or a
  duplicate id fails fast.
- `ScanAssembly` instantiates marked classes that implement the handler
  interface with a parameterless constructor. A marked but non-conforming
  class fails fast.
- The registry is built at startup. `Freeze` locks it. A later `Add` throws.

## Dispatch

- A direction mismatch answers `MethodNotFound`.
- A payload that does not decode answers `InvalidParams`.
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
    bool TryDeserialize(ReadOnlySpan<byte> data, out T value);
}
```

- The default implementation wraps the generated nsd methods. A per-type
  cache holds one serializer per payload type.
- `TryDeserialize` returns a result. It never throws.

## Streaming consumption

- Callback core: `InvokeStreamAsync` with an `IChunkSink<TChunk>`. The sink
  owns the buffering policy.
- Wrapper: `RpcStreamSession` gives `Chunks` as `IAsyncEnumerable<TChunk>`
  and `Response` as a `Task`. The queue is bounded by `MaxBufferedChunks`. A
  full queue faults the stream with `Busy`.
- Cancelling the token cancels both halves.
