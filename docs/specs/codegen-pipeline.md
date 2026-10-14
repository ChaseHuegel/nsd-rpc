# nsdc Build Pipeline

One subject: how the build runs the `nsdc` compiler and how generated types
join the compile. This wiring is arcane. Change it only with a test.

## Contract

Source of truth: [NsdRpc/NsdRpc.csproj:62-74](../../NsdRpc/NsdRpc.csproj).

1. `nsdc` must be on the PATH. The build fails without it. That is the
   contract.
2. The `NsdcCodegen` target runs before `CoreCompile` in every compiling
   project instance. Generated types exist before any compile step, including
   on a fresh clone and for `-f` builds.
3. The target deletes `CodeGen/Output/`, recreates it, and runs
   `nsdc -r -i Nsd -o CodeGen/Output`. This cleans stale generated files when
   a schema loses a type.
4. The target prepends `#nullable enable` to every generated file. nsdc 1.3.3
   emits nullable annotations without the directive, which fails the build
   with CS8669. The step is a workaround. Remove it when nsdc emits the
   directive itself. See
   [issue 0009](../issues/0009-nsdc-nullable-directive.md).
5. The target adds the generated `.cs` files to `Compile` at target time.
   An eval-time glob would see an empty directory on a first build.
6. The library sets `BuildInParallel` to `false`
   ([csproj:23](../../NsdRpc/NsdRpc.csproj)). The target rewrites a shared
   directory. Parallel per-framework builds would race on it. The codegen
   therefore runs once per framework, in order.
7. `CodeGen/Output/` is untracked. See [.gitignore](../../.gitignore).

The codegen also runs in IDE design-time builds. An IDE needs `nsdc` on the
PATH for full IntelliSense on generated types.

## Overrides

- `NsdcTool` property: a custom path to the compiler. Default `nsdc`. See
  [csproj:59](../../NsdRpc/NsdRpc.csproj).

## CI

Each workflow downloads the pinned nsdc release and adds it to the PATH.

- Version pin: `NSDC_VERSION` in each workflow. Current: `1.3.3`.
- Download source: the needlefish GitHub releases.

The tests pin the pipeline end to end. CI builds fail when codegen fails.
See [NsdRpc.Tests/HandshakeTests.cs:8](../../NsdRpc.Tests/HandshakeTests.cs).

## Bumping nsdc

1. Change `NSDC_VERSION` in
   [build.yml](../../.github/workflows/build.yml),
   [test.yml](../../.github/workflows/test.yml), and
   [publish.yml](../../.github/workflows/publish.yml).
2. Update the pinned version in [development.md](../development.md).
3. Build and test locally with the new compiler.
