# Development

One subject: how to work in this repository. It covers the toolchain, build
and test workflow, commit rules, config conventions, and the docs pass.

The coding style rules live in [specs/coding-style](specs/coding-style.md).
The testing rules live in [testing](testing.md).

## Toolchain

- Use the .NET 10 SDK. The library targets `netstandard2.1;net10.0`. Tests
  target `net10.0`.
- Schema generation comes from the `Needlefish.Generators` NuGet package.
  Restore pulls it. No compiler install is needed. Pin the version in the
  csproj files. See [specs/codegen-pipeline](specs/codegen-pipeline.md).
- No `global.json`, `Directory.Build.props`, or `.editorconfig` exist. Each
  `.csproj` is self-contained.
- No lint, format, or typecheck scripts. `dotnet build` is the check. It
  compiles with warnings as errors and analyzers at their strictest level.
  Zero warnings allowed. See
  [specs/coding-style](specs/coding-style.md).

## Build and test

```bash
dotnet build
dotnet build --configuration Release
dotnet test                                         # xunit, edge-case tests
dotnet pack NsdRpc/NsdRpc.csproj -o ./.packages/    # NuGet package
dotnet pack NsdRpc.Generators/NsdRpc.Generators.csproj -o ./.packages/
```

The build compiles the schemas with the `Needlefish.Generators` source
generator. See [specs/codegen-pipeline](specs/codegen-pipeline.md).

The `NsdRpc.Generators` package is a source generator. Consuming projects
reference it as an analyzer:

```xml
<ProjectReference Include="NsdRpc.Generators" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

The generator emits typed service clients. See
[specs/rpc-api](specs/rpc-api.md).

## Publishing

Publishing runs on `v*` tags only. The workflow uses NuGet trusted
publishing. It exchanges a GitHub OIDC token for a short-lived NuGet API
key. No long-lived secret is stored.

To publish a package the first time:

1. Bump the `Version` property in the package csproj.
2. Tag the commit. Example: `v0.1.0`.
3. In nuget.org, add a trusted publisher for the package. Enter the GitHub
   owner, the repository, and `.github/workflows/publish.yml`. Do this once
   per package. The repository ships three: `NsdRpc`, `NsdRpc.Tcp`, and
   `NsdRpc.Generators`.
4. Store the nuget.org profile name as the `NUGET_USER` secret in the
   repository. Use the profile name, not the email address.

The push step uses the temporary key from the login step. It never reads a
stored API key.

## Code style

All hand-written code follows Tiger Style and the Power of 10, mapped to C#.
See [specs/coding-style](specs/coding-style.md). That doc wins over any other
doc on style questions.

Repo-specific rules:

- Never hand-edit generated wire types. Change the `.nsd` schema instead. See
  [specs/nsd-schemas](specs/nsd-schemas.md).

## Testing

Tests must exercise edge cases. No flimsy tests. See
[testing](testing.md).

## Commit rules

Commit messages inform the reader. `git blame` shows them. Pull request
descriptions do not.

- Subject: imperative mood, capitalized, at most 72 characters. No prefixes
  (`feat:`/`fix:`), no emojis, no trailing period.
- Body: say what changed and why. State the motivation and the tradeoffs.
- One commit per change. Do not mix unrelated changes.
- When a change closes a local issue, append its number to the subject.
  Example: `Add handshake message bounds checking #1`.

## Config conventions

- Any new tunable is a config key, never a hardcoded constant.
- Every limit (queue size, buffer size, loop cap) is a config key.
- Document every key in a schema table in the same change that adds it.
  Tables live in `docs/specs/`.

## Docs pass (mandatory)

Every change set ends with a docs pass, run before finalizing.

1. Map the changed behavior to its doc via the subject index
   ([docs/README.md](README.md)).
2. Update the matching doc, or create a new one if none covers the behavior.
   New docs go in `docs/specs/`. Keep one subject per doc.
3. Refresh the source-of-truth `file:line` pointers so the next reader lands
   on the new code.
4. Do not document what is obvious. Docs exist to make the non-obvious
   explicit.
5. Never duplicate content across docs. Link instead of copying.

The pass is mandatory when the change touches a format, a config key, a
protocol rule, or the build pipeline. Attribute the docs pass to the same
change that caused it. Stale docs are a bug.

## Writing style

All new doc prose uses Simplified Technical English. Use one name per thing.
Use active voice. Use short sentences. Use no semicolons and no contractions.
