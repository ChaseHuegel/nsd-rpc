# Architecture

One subject: the as-built structure of this repository. This doc records the
container structure and where each piece goes. Update it as code lands.

## Project map

| Project | Path | Role |
|---|---|---|
| NsdRpc | [NsdRpc/NsdRpc.csproj](../../NsdRpc/NsdRpc.csproj) | The library. Zero dependencies. Ships as the `NsdRpc` NuGet package. |
| NsdRpc.Tcp | [NsdRpc.Tcp/NsdRpc.Tcp.csproj](../../NsdRpc.Tcp/NsdRpc.Tcp.csproj) | The TCP transport: framing, sockets, and the handshake. References the core. Zero dependencies. |
| NsdRpc.Generators | [NsdRpc.Generators/NsdRpc.Generators.csproj](../../NsdRpc.Generators/NsdRpc.Generators.csproj) | The source generator that emits typed service clients. Ships as the `NsdRpc.Generators` analyzer package. |
| NsdRpc.Tests | [NsdRpc.Tests/NsdRpc.Tests.csproj](../../NsdRpc.Tests/NsdRpc.Tests.csproj) | xunit tests. References both libraries. Not packed. |
| NsdRpc.Generators.Tests | [NsdRpc.Generators.Tests/NsdRpc.Generators.Tests.csproj](../../NsdRpc.Generators.Tests/NsdRpc.Generators.Tests.csproj) | xunit tests for the generator. Not packed. |
| Chat.Common | [samples/Chat.Common](../../samples/Chat.Common/Chat.Common.csproj) | The shared chat wire contract and fake data. Not packed. |
| Chat.Raw | [samples/Chat.Raw](../../samples/Chat.Raw/Chat.Raw.csproj) | The raw-API chat sample. Not packed. |
| Solution | [NsdRpc.sln](../../NsdRpc.sln) | Contains all projects. |

The libraries target `netstandard2.1;net10.0`. The generator targets
`netstandard2.0`. Tests and samples target `net10.0`.

## Build pipeline

The build runs `nsdc` on the schemas in `NsdRpc/Nsd/` before every compile.
Generated types land in `NsdRpc/CodeGen/Output/` and join the compile.
The output is untracked. See
[specs/codegen-pipeline](specs/codegen-pipeline.md) for the full contract.

## Wire format

The wire format is nsd (Needlefish Schema Definition). Schemas are the source
of truth. nsdc compiles them into C# structs with serialize and deserialize
methods. See [specs/nsd-schemas](specs/nsd-schemas.md) for the contract and
the current schema. The RPC envelope builds on the format. See
[specs/rpc-protocol](specs/rpc-protocol.md).

## NuGet packaging

Three packages ship: `NsdRpc`, `NsdRpc.Tcp`, and `NsdRpc.Generators`. Each
carries its own package metadata and `Version` in its csproj. Pack with
`dotnet pack` per project, or let CI do it. CI packs and pushes to
nuget.org only on `v*` tags, using trusted publishing. See
[.github/workflows/publish.yml](../../.github/workflows/publish.yml) and
[development](development.md).

## Static analysis

Both projects compile with warnings as errors and Roslyn analyzers at their
strictest level. Zero warnings allowed. The CI build is the daily static
analysis gate. See [specs/coding-style](specs/coding-style.md).

## Module map

| Module | Source | Responsibility |
|---|---|---|
| Protocol | `NsdRpc/Protocol/` | The hand-written wire enums: `RpcMessageKind`, `RpcErrorCode`. |
| Transport | `NsdRpc/Transport/`, `NsdRpc/RpcOptions.cs` | The connection and listener interfaces, the clock, and peer configuration. |
| Engine | `NsdRpc/Engine/` | The peer, correlation, timeouts, cancellation, faults, payload codecs, and stream sessions. |
| Handlers | `NsdRpc/Handlers/` | The method attribute, handler interface, registry, and dispatcher. |
| Composition | `NsdRpc/RpcClient.cs`, `NsdRpc/RpcServer.cs` | The client and server roots that bind a peer, a transport, and a registry. |
| Tcp transport | `NsdRpc.Tcp/` | The frame decoder, socket engine, connections, and the handshake. |
| Service generator | `NsdRpc.Generators/` | The `RpcService` source generator, its diagnostics, and the marker attribute. |
| Invariant | `NsdRpc/Invariant.cs` | The always-on assert helper. |

## CI

- [build.yml](../../.github/workflows/build.yml) builds on push and PRs to
  `main`.
- [test.yml](../../.github/workflows/test.yml) builds and tests on push and
  PRs to `main`.
- [publish.yml](../../.github/workflows/publish.yml) tests, packs, and pushes
  to nuget.org on `v*` tags only.
