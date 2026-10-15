# Improvement: Migrate schema generation to the needlefish source generator

- Type: improvement
- Status: done
- Workflow: ../specs/issues.md

## Problem

The build runs the `nsdc` compiler as a pre-compile step from a csproj
target. The needlefish project now ships `Needlefish.Generators`, a Roslyn
source generator that discovers `.nsd` files and generates the same types
during compilation. The explicit compiler step, the PATH requirement, and
the CI install step all go away.

## Acceptance criteria

- [x] `NsdRpc.csproj` drops the `NsdcCodegen` target and references `Needlefish.Generators` with `PrivateAssets="all"` at a pinned version.
- [x] `Chat.Common.csproj` drops the copied target and references the generator directly.
- [x] The `CodeGen/Output` directories are deleted and the `.gitignore` entry is removed.
- [x] The `BuildInParallel=false` constraint is removed.
- [x] The three workflows lose the nsdc install step and `NSDC_VERSION`.
- [x] `codegen-pipeline.md` describes the generator, not the compiler step; `development.md`, `architecture.md`, `nsd-schemas.md`, and `AGENTS.md` are updated.
- [x] The wire-contract tests pass unchanged, proving byte-identical output.
- [x] Both chat samples build and run end to end.
