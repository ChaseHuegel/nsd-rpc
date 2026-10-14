# Development

One subject: how to work in this repository. It covers the toolchain, build
and test workflow, commit rules, config conventions, and the docs pass.

The coding style rules live in [specs/coding-style](specs/coding-style.md).
The testing rules live in [testing](testing.md).

## Toolchain

- Use the .NET 10 SDK. The library targets `netstandard2.1;net10.0`. Tests
  target `net10.0`.
- The `nsdc` compiler must be on your PATH. Pin the version used by CI:
  currently v1.3.3 from the
  [needlefish releases](https://github.com/ChaseHuegel/needlefish/releases).
  Bump `NSDC_VERSION` in the workflows when you bump it here.
- No `global.json`, `Directory.Build.props`, or `.editorconfig` exist. Each
  `.csproj` is self-contained.
- No lint, format, or typecheck scripts. `dotnet build` is the check. It
  compiles with warnings as errors and analyzers at their strictest level.
  Zero warnings allowed. See
  [specs/coding-style](specs/coding-style.md).

## Build and test

```bash
dotnet build                                        # runs nsdc codegen first
dotnet build --configuration Release
dotnet test                                         # xunit, edge-case tests
dotnet pack NsdRpc/NsdRpc.csproj -o ./.packages/    # NuGet package
```

The build runs `nsdc` before any compile step. Generated code lands in
`NsdRpc/CodeGen/Output/`, which is untracked. See
[specs/codegen-pipeline](specs/codegen-pipeline.md) for the full contract.

## Code style

All hand-written code follows Tiger Style and the Power of 10, mapped to C#.
See [specs/coding-style](specs/coding-style.md). That doc wins over any other
doc on style questions.

Repo-specific rules:

- Never hand-edit `.cs` files under `NsdRpc/CodeGen/Output/`. Change the
  `.nsd` schema instead. See [specs/nsd-schemas](specs/nsd-schemas.md).
- Never commit the output directory.

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
