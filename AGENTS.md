# AGENTS.md

Rules for agents working in this repository. Read this before you change
anything.

## Repository index

| Path | What it is |
|---|---|
| `.github/workflows/` | CI/CD: build, test, and publish (publish runs on `v*` tags only). |
| `NsdRpc/` | The library. Zero dependencies. Holds the nsd schemas. |
| `NsdRpc.Tests/` | xunit tests. |
| `docs/` | The documentation system. Subject index at `docs/README.md`. |
| `AGENTS.md` | This file. |
| `LICENSE` | MIT. |
| `README.md` | Project front page. |
| `NsdRpc.sln` | The solution. |
| `.gitignore` | Ignored paths. |

## First stops

| Goal | Doc |
|---|---|
| Build, test, or commit | [docs/development.md](docs/development.md) |
| Write any code | [docs/specs/coding-style.md](docs/specs/coding-style.md) |
| Write any test | [docs/testing.md](docs/testing.md) |
| Understand the project layout | [docs/architecture.md](docs/architecture.md) |
| Change the wire format or add a message | [docs/specs/nsd-schemas.md](docs/specs/nsd-schemas.md) |
| Work on the RPC protocol or engine | [docs/specs/rpc-protocol.md](docs/specs/rpc-protocol.md) |
| Touch schema generation or bump the generator | [docs/specs/codegen-pipeline.md](docs/specs/codegen-pipeline.md) |
| Find or add work | [docs/issues/index.md](docs/issues/index.md) |

## Quality gates

Run these before you finish any change:

- `dotnet build` succeeds with zero warnings. Warnings are errors.
- `dotnet test` passes. Tests must exercise edge cases per
  [docs/testing.md](docs/testing.md). No flimsy tests.
- The docs pass is done. Stale docs are a bug. See
  [docs/development.md](docs/development.md).

## Rules

- Fetch only the doc you need via the index. Never fetch the whole tree.
- Follow [docs/specs/coding-style.md](docs/specs/coding-style.md) for all
  hand-written code.
- Change the wire types only through the `.nsd` schema. See
  [docs/specs/nsd-schemas.md](docs/specs/nsd-schemas.md).
- The library has zero runtime dependencies. Build-time tooling is a
  `PrivateAssets` reference and does not ship.
- Validate every parameter and check every return value. Handle every error.
- Work one issue at a time. Follow
  [docs/specs/issues.md](docs/specs/issues.md). Report adjacent problems to
  the user and file them in the Backlog. Do not fix them silently.
- Commit messages explain what and why. See
  [docs/development.md](docs/development.md).
- Write all doc prose in Simplified Technical English. Use one name per
  thing. Use active voice. Use short sentences. Use no semicolons and no
  contractions.
- Keep each doc on one subject. Link instead of copying.
