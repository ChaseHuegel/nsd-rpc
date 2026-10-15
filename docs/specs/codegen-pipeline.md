# Schema Generation

One subject: how the needlefish source generator produces the wire types from
the schema files. There is no separate compiler step.

## Contract

Source of truth: [NsdRpc/NsdRpc.csproj:40](../../NsdRpc/NsdRpc.csproj).

1. The library references the `Needlefish.Generators` package with
   `PrivateAssets="all"`. The generator compiles every `*.nsd` file in the
   project during compilation.
2. The reference is build-time only. It is not a shipped dependency, so the
   `NsdRpc` package keeps its zero-dependency property.
3. The generator discovers `**/*.nsd` automatically. Editing a schema updates
   IntelliSense without a rebuild.
4. The library keeps `AllowUnsafeBlocks` because the generated code uses raw
   pointers. The generated code compiles against `netstandard2.1`.

## Overrides

- `NeedlefishPartial` property: generate the types with the `partial`
  keyword. Default `false`.
- `NeedlefishEnableDefaultItems`: set to `false` to manage the file list
  yourself through `AdditionalFiles`.
- Per-file `NsdFiles` metadata sets an option for a single schema.

The full option list lives in the
[needlefish README](https://github.com/ChaseHuegel/needlefish#readme).

## Version pin

The generator version is pinned in the csproj files, not in the workflows.
Current: `1.2.0`. Bump it in `NsdRpc/NsdRpc.csproj` and
`samples/Chat.Common/Chat.Common.csproj` together.

## CI

CI runs `dotnet build` and `dotnet test`. No compiler install step is needed;
restore pulls the generator package. The tests pin the generated code end to
end. See [NsdRpc.Tests/HandshakeTests.cs:8](../../NsdRpc.Tests/HandshakeTests.cs).

## Bumping the generator

1. Change the `Needlefish.Generators` version in both csproj files.
2. Build and test. The wire-contract tests fail if the output drifts.
