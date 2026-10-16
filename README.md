# nsd-rpc

A zero-dependency C# RPC framework and library built on
[Needlefish](https://github.com/ChaseHuegel/needlefish) (nsd). Schemas in
`.nsd` files define the wire messages. The `Needlefish.Generators` source
generator compiles them at build time.

[![build](https://github.com/ChaseHuegel/nsd-rpc/actions/workflows/build.yml/badge.svg)](https://github.com/ChaseHuegel/nsd-rpc/actions/workflows/build.yml)
[![test](https://github.com/ChaseHuegel/nsd-rpc/actions/workflows/test.yml/badge.svg)](https://github.com/ChaseHuegel/nsd-rpc/actions/workflows/test.yml)

## Install

```bash
dotnet add package NsdRpc
dotnet add package NsdRpc.Tcp
```

The source generator `NsdRpc.Generators` is referenced as an analyzer. Your
own schemas compile with `Needlefish.Generators`. See
[getting started](docs/getting-started.md) for the full project setup.

## Quick start

Reference the generator as an analyzer, then declare a service. The shape of
each method decides if it is a client call or a server handler.

```xml
<PackageReference Include="NsdRpc.Generators" Version="0.1.0"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

```csharp
[RpcService]
public interface IGreeter
{
    [RpcMethod(1)]
    Task<RpcResult<HelloResponse>> SayHelloAsync(
        HelloRequest request, CancellationToken cancellationToken = default);

    [RpcMethod(1)]
    ValueTask<RpcResult<HelloResponse>> SayHelloAsync(
        HelloRequest request, RpcContext context, CancellationToken cancellationToken);
}

public sealed class Greeter : GreeterHandler
{
    public override ValueTask<RpcResult<HelloResponse>> SayHelloAsync(
        HelloRequest request, RpcContext context, CancellationToken cancellationToken)
    {
        var response = new HelloResponse("hello " + request.Name);
        return ValueTask.FromResult(RpcResult<HelloResponse>.Success(response));
    }
}
```

Start a server, then call it from a client.

```csharp
var registry = new RpcRegistry();
registry.AddService(new Greeter());
registry.Freeze();

var transport = new TcpServerTransport(new RpcOptions(), new TcpOptions());
var server = new RpcServer(transport, registry);
server.Start();

var connection = new TcpClientConnection(
    new RpcOptions(), new TcpOptions { Host = "127.0.0.1", Port = transport.Port });
await connection.ConnectAsync();

var clientRegistry = new RpcRegistry();
clientRegistry.Freeze();
var client = new RpcClient(connection, clientRegistry);

var greeter = new GreeterClient(client);
RpcResult<HelloResponse> result = await greeter.SayHelloAsync(new HelloRequest("world"));
```

`HelloRequest` and `HelloResponse` are messages from a `.nsd` schema. See
[getting started](docs/getting-started.md) for the schema and the bare API.

## Build

Prerequisites:

- .NET 10 SDK

```bash
dotnet build
dotnet test
```

The build compiles `NsdRpc/Nsd/*.nsd` with the `Needlefish.Generators` source
generator. There is no separate compiler step.

## Docs

Start with the subject index at [docs/README.md](docs/README.md). For
consumers:
[getting started](docs/getting-started.md) and the
[quick reference](docs/quick-reference.md). Work is tracked in
[docs/issues/index.md](docs/issues/index.md).

## License

[MIT](LICENSE)
