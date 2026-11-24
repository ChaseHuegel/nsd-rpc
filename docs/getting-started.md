# Getting Started

One subject: install the packages and run a first client and a first server.

This guide uses the `RpcService` source generator. Several steps also show the
bare-API path. The full programs live in
[samples/Chat.Generated](../samples/Chat.Generated) and
[samples/Chat.Raw](../samples/Chat.Raw). For the complete API surface, read the
[quick reference](quick-reference.md).

## Prerequisites

- The .NET 10 SDK. The libraries also support .NET Standard 2.1.
- One project, or a server project and a client project. The server and the
  client can run in one process.

## Step 1: Install the packages

Add these packages to the project.

```xml
<PackageReference Include="NsdRpc" Version="0.1.0" />
<PackageReference Include="NsdRpc.Tcp" Version="0.1.0" />
<PackageReference Include="NsdRpc.Generators" Version="0.1.0"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
<PackageReference Include="Needlefish.Generators" Version="1.2.0" />
```

- `NsdRpc` holds the engine, the handlers, and the wire types.
- `NsdRpc.Tcp` holds the TCP transport.
- `NsdRpc.Generators` is a source generator. Set
  `OutputItemType="Analyzer"` and `ReferenceOutputAssembly="false"`. Do not
  reference it as a library.
- `Needlefish.Generators` compiles your `.nsd` schemas. See
  [codegen-pipeline](specs/codegen-pipeline.md).

`NsdRpc` and `NsdRpc.Tcp` have no runtime dependencies.

## Step 2: Define a schema

The wire messages come from `.nsd` files. Add a `Greeting.nsd` file with a
request and a response.

```nsd
#version 1.0;
#namespace QuickStart;

message HelloRequest
{
    string Name = 0;
}

message HelloResponse
{
    string Message = 0;
}
```

Build the project. The generator emits one C# struct per message. Never edit
the generated code. See [nsd-schemas](specs/nsd-schemas.md).

## Step 3: Declare the service

The generator path: declare one `[RpcService]` interface. The shape of each
method decides its role. A shape with no `RpcContext` is a client invocation.
A shape with an `RpcContext` is a server handler.

```csharp
using NsdRpc;

namespace QuickStart;

[RpcService]
public interface IGreeter
{
    // Invocation shape. Generates the GreeterClient method.
    [RpcMethod(1)]
    Task<RpcResult<HelloResponse>> SayHelloAsync(
        HelloRequest request, CancellationToken cancellationToken = default);

    // Handler shape. Generates the GreeterHandler member.
    [RpcMethod(1)]
    ValueTask<RpcResult<HelloResponse>> SayHelloAsync(
        HelloRequest request, RpcContext context, CancellationToken cancellationToken);
}
```

The generator emits `GreeterClient` and `GreeterHandler` in the interface
namespace. The client name drops the leading `I`. See
[rpc-api](specs/rpc-api.md).

The bare-API path: skip the interface. Put `[RpcMethod]` on the handler class
instead. See step 4.

## Step 4: Write a handler

The generator path: subclass the generated base. Override only the methods you
handle. An unhandled call answers `InternalError`. An unhandled notification
does nothing.

```csharp
public sealed class MyGreeterHandler : GreeterHandler
{
    public override ValueTask<RpcResult<HelloResponse>> SayHelloAsync(
        HelloRequest request, RpcContext context, CancellationToken cancellationToken)
    {
        var response = new HelloResponse("hello " + request.Name);
        return ValueTask.FromResult(RpcResult<HelloResponse>.Success(response));
    }
}
```

The bare-API path: implement `IRpcHandler<TRequest, TResponse>` and mark the
class with `[RpcMethod]`.

```csharp
[RpcMethod(1)]
public sealed class HelloHandler : IRpcHandler<HelloRequest, HelloResponse>
{
    public ValueTask<RpcResult<HelloResponse>> ExecuteAsync(
        HelloRequest request, RpcContext context, CancellationToken cancellationToken)
    {
        var response = new HelloResponse("hello " + request.Name);
        return ValueTask.FromResult(RpcResult<HelloResponse>.Success(response));
    }
}
```

Return a result for a call. Keep `context` and call `context.Respond(result)`
later for a deferred answer. Call `context.Stream(chunk)` to send stream chunks
before the response. Expected failures are results, not exceptions.

## Step 5: Start the server

Build the registry, freeze it, and start the server.

```csharp
using NsdRpc;
using NsdRpc.Tcp;

var registry = new RpcRegistry();
registry.AddService(new MyGreeterHandler()); // Generator path.
// registry.Add(new HelloHandler());          // Bare-API path.
registry.Freeze();

var transport = new TcpServerTransport(new RpcOptions(), new TcpOptions());
var server = new RpcServer(transport, registry);
server.Start();

int port = transport.Port; // The bound port. Set TcpOptions.Port to fix it.
```

`TcpOptions.Port` defaults to 0, so the server binds an ephemeral port. Call
`transport.Port` to read the bound port. That call starts the listener when
needed. A later `Add` throws after `Freeze`.

## Step 6: Connect a client

A client holds a registry too. That registry handles the calls and the
notifications the server sends.

```csharp
var connection = new TcpClientConnection(
    new RpcOptions(),
    new TcpOptions { Host = "127.0.0.1", Port = port });
await connection.ConnectAsync();

var clientRegistry = new RpcRegistry();
clientRegistry.Freeze();

var client = new RpcClient(connection, clientRegistry);
```

`ConnectAsync` runs the transport handshake. Own the `RpcClient` for the life
of the connection. It owns the id space.

## Step 7: Call the service

The generator path reads like an ordinary method call.

```csharp
var greeter = new GreeterClient(client);
RpcResult<HelloResponse> result = await greeter.SayHelloAsync(new HelloRequest("world"));
if (result.IsSuccess)
{
    Console.WriteLine(result.Value.Message);
}
```

The bare-API path passes the method id and the request.

```csharp
RpcResult<HelloResponse> result =
    await client.InvokeAsync<HelloRequest, HelloResponse>(1, new HelloRequest("world"));
```

Check `result.IsSuccess` before you read `result.Value`. Cast
`result.ErrorCode` to `RpcErrorCode` for a named reason.

## Other call kinds

A notification sends a message with no response. A `void` or `bool` invocation
shape generates a client method with the same name. A `bool` method returns
`true` when the peer accepts the frame and `false` when it rejects the frame. A
`void` handler shape generates a virtual member on the handler base. An
unhandled notification does nothing.

For a notification method named `NotifyHello`:

```csharp
greeter.NotifyHello(new HelloRequest("world"));  // Generator path.
client.Notify(2, new HelloRequest("world"));     // Bare-API path.
```

A streamed call sends chunks before the response. It has two consumption
paths. Pass an `IChunkSink<TChunk>` to receive a callback per chunk. Or take
an `RpcStreamSession<TChunk, TResponse>` and read its `Chunks` and `Response`.

The full streaming example lives in the chat samples. See
[samples/README.md](../samples/README.md) and
[rpc-api](specs/rpc-api.md).

## Next steps

- [quick-reference](quick-reference.md) lists the full public API.
- [samples/Chat.Generated](../samples/Chat.Generated) shows one interface that
  drives a whole chat app.
- [samples/Chat.Raw](../samples/Chat.Raw) shows the same app over the bare API.
- [rpc-protocol](specs/rpc-protocol.md) records the wire contract.
