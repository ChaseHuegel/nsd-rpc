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
```

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

Start with the subject index at [docs/README.md](docs/README.md). Work is
tracked in [docs/issues/index.md](docs/issues/index.md).

## License

[MIT](LICENSE)
