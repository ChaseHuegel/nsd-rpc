# Architecture

One subject: the as-built structure of this repository. This doc records the
container structure and where each piece goes. Update it as code lands.

## Project map

| Project | Path | Role |
|---|---|---|
| NsdRpc | [NsdRpc/NsdRpc.csproj](../../NsdRpc/NsdRpc.csproj) | The library. Zero dependencies. Ships as the `NsdRpc` NuGet package. |
| NsdRpc.Tests | [NsdRpc.Tests/NsdRpc.Tests.csproj](../../NsdRpc.Tests/NsdRpc.Tests.csproj) | xunit tests. References the library. Not packed. |
| Solution | [NsdRpc.sln](../../NsdRpc.sln) | Contains both projects. |

The library targets `netstandard2.1;net10.0`. Tests target `net10.0`.

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

Package metadata lives in
[NsdRpc/NsdRpc.csproj:26-36](../../NsdRpc/NsdRpc.csproj). Version is set
there. Pack with `dotnet pack NsdRpc/NsdRpc.csproj -o ./.packages/`. CI packs
and pushes to nuget.org only on `v*` tags. See
[.github/workflows/publish.yml](../../.github/workflows/publish.yml).

## Static analysis

Both projects compile with warnings as errors and Roslyn analyzers at their
strictest level. Zero warnings allowed. The CI build is the daily static
analysis gate. See [specs/coding-style](specs/coding-style.md).

## Module map

| Module | Source | Responsibility |
|---|---|---|
| Protocol | `NsdRpc/Protocol/` | The hand-written wire enums: `RpcMessageKind`, `RpcErrorCode`. |
| Transport | `NsdRpc/Transport/`, `NsdRpc/RpcOptions.cs` | The connection and listener interfaces, the clock, and peer configuration. |
| Invariant | `NsdRpc/Invariant.cs` | The always-on assert helper. |

## CI

- [build.yml](../../.github/workflows/build.yml) builds on push and PRs to
  `main`.
- [test.yml](../../.github/workflows/test.yml) builds and tests on push and
  PRs to `main`.
- [publish.yml](../../.github/workflows/publish.yml) tests, packs, and pushes
  to nuget.org on `v*` tags only.
