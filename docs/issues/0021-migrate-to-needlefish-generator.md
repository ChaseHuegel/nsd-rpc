# Improvement: Migrate schema generation to the needlefish source generator

- Type: improvement
- Status: open
- Workflow: ../specs/issues.md

## Problem

The build runs the `nsdc` compiler as a pre-compile step from a csproj
target. The needlefish project now ships `Needlefish.Generators`, a Roslyn
source generator that discovers `.nsd` files and generates the same types
during compilation. The explicit compiler step, the PATH requirement, and
the CI install step all go away.

## Acceptance criteria

- [ ] `NsdRpc.csproj` drops the `NsdcCodegen` target and references `Needlefish.Generators` with `PrivateAssets="all"` at a pinned version.
- [ ] `Chat.Common.csproj` drops the copied target and references the generator directly.
- [ ] The `CodeGen/Output` directories are deleted and the `.gitignore` entry is removed.
- [ ] The `BuildInParallel=false` constraint is removed.
- [ ] The three workflows lose the nsdc install step and `NSDC_VERSION`.
- [ ] `codegen-pipeline.md` describes the generator, not the compiler step; `development.md`, `architecture.md`, `nsd-schemas.md`, and `AGENTS.md` are updated.
- [ ] The wire-contract tests pass unchanged, proving byte-identical output.
- [ ] Both chat samples build and run end to end.
