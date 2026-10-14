# AGENTS.md

Rules for agents working in this repository. Read this before you change
anything.

## Repository index

| Path | What it is |
|---|---|
| `.github/workflows/` | CI/CD: build, test, and publish (publish runs on `v*` tags only). |
| `NsdRpc/` | The library. Zero dependencies. Holds the nsd schemas and codegen wiring. |
| `NsdRpc.Tests/` | xunit tests. |
| `docs/` | The documentation system. Subject index at `docs/README.md`. |
| `AGENTS.md` | This file. |
| `LICENSE` | MIT. |
| `README.md` | Project front page. |
| `NsdRpc.sln` | The solution. |
| `.gitignore` | Ignored paths, including the generated code output. |

## First stops

| Goal | Doc |
|---|---|
| Build, test, or follow code style | [docs/development.md](docs/development.md) |
| Understand the project layout | [docs/architecture.md](docs/architecture.md) |
| Change the wire format or add a message | [docs/specs/nsd-schemas.md](docs/specs/nsd-schemas.md) |
| Touch the build pipeline or bump nsdc | [docs/specs/codegen-pipeline.md](docs/specs/codegen-pipeline.md) |
| Find or add work | [docs/issues/index.md](docs/issues/index.md) |

## Quality gates

Run these before you finish any change:

- `dotnet build` succeeds. The build runs `nsdc` codegen. `nsdc` must be on
  the PATH.
- `dotnet test` passes.
- The docs pass is done. Stale docs are a bug. See
  [docs/development.md](docs/development.md).

## Rules

- Fetch only the doc you need via the index. Never fetch the whole tree.
- Never hand-edit files under `NsdRpc/CodeGen/Output/`. Edit the `.nsd`
  schema instead. The output directory is untracked.
- The library has zero dependencies. Do not add a package reference to
  `NsdRpc/NsdRpc.csproj`.
- Work one issue at a time. Follow
  [docs/specs/issues.md](docs/specs/issues.md). Report adjacent problems to
  the user and file them in the Backlog. Do not fix them silently.
- Follow the code style and commit rules in
  [docs/development.md](docs/development.md).
- Write all doc prose in Simplified Technical English. Use one name per
  thing. Use active voice. Use short sentences. Use no semicolons and no
  contractions.
- Keep each doc on one subject. Link instead of copying.
