# Development

One subject: how to work in this repository. It covers the toolchain, build
and test workflow, code style, commit rules, config conventions, and the docs
pass.

## Toolchain

- Use the .NET 10 SDK. The library targets `netstandard2.1;net10.0`. Tests
  target `net10.0`.
- The `nsdc` compiler must be on your PATH. Pin the version used by CI:
  currently v1.3.2 from the
  [needlefish releases](https://github.com/ChaseHuegel/needlefish/releases).
  Bump the `NSDC_VERSION` in the workflows when you bump it here.
- No `global.json`, `Directory.Build.props`, or `.editorconfig` exist. Each
  `.csproj` is self-contained.
- No lint, format, or typecheck scripts. `dotnet build` is the check.

## Build and test

```bash
dotnet build                                         # runs nsdc codegen first
dotnet build --configuration Release
dotnet test                                          # xunit
dotnet pack NsdRpc/NsdRpc.csproj -o ./.packages/     # NuGet package
```

The build runs `nsdc` before any compile step. The generated code goes to
`NsdRpc/CodeGen/Output/`. That directory is untracked. See
[specs/codegen-pipeline](specs/codegen-pipeline.md) for the full contract.

## Code style

These conventions apply to all hand-written code.

### Names and formatting

| Element | Convention | Example |
|---|---|---|
| Namespace | File-scoped | `namespace NsdRpc;` |
| Classes, structs, records | PascalCase | `RpcClient` |
| Interfaces | PascalCase, `I` prefix | `IRpcTransport` |
| Methods | PascalCase | `TrySend<T>()` |
| Properties | PascalCase | `MaxLength` |
| Public fields | PascalCase, rare | `ProtocolVersion` |
| Private fields | `_` + camelCase | `_pending` |
| Private constants | `UPPER_SNAKE_CASE` | `MAX_BUFFER_SIZE` |
| Parameters | camelCase | `payload` |
| Locals | camelCase, `var` when obvious | `var buffer` |
| Enums | PascalCase for type and members | `CallStatus.Ok` |

Prefer domain-meaningful names. Pure computations use `Get*`. Create-or-update
methods use `AddOrUpdate*`.

### Braces and layout

- Allman style. The opening brace sits on its own line for all blocks.
- Single-line accessors and lambdas use expression bodies (`=>`).
- All blocks have braces.
- With `ImplicitUsings` on, omit redundant `using` directives.

### Language features

- Never qualify with `this.`. Use a different parameter name instead.
- Mark every field set once as `readonly`.
- Mark utility and leaf classes as `sealed`.
- Use the `in` modifier on value types passed to constructors and methods.
- Prefer `internal` access. Make types `public` only for a real cross-assembly
  need.
- Nullable reference types are on everywhere. Do not disable them.
- Prefer primary constructors for small value and state types.
- Prefer collection expressions (`[]`) over `new List<T>()`.
- Use named arguments when a value's meaning is not self-evident.

### Null handling

- Use `== null` and `!= null` for checks. Do not use `is null` or `is not null`.
- Use `?.` for null-conditional calls on delegates and events.
- Use `??` and `??=` for coalescing.
- Use `?? throw` for guard clauses in constructors.

### Types

- Use structs for small data holders and value types.
- Use classes for services, managers, and long-lived objects.
- Use `readonly struct` for immutable value types.
- One type per file.
- Use the most concrete type that fits. Prefer `T[]`, `List<T>`, and
  `IReadOnlyList<T>` over `IEnumerable<T>` as declared types.
- Never expose a collection a caller can mutate.
- Do not use records on the `netstandard2.1` target.

### Exceptions

- Use guard clauses with `?? throw` or an early `throw`.
- Do not use try/catch for expected control flow. Return a `Result<T>`.
- Use an outer try/catch only at module boundaries, for logging and graceful
  degradation.

### Testing

- Use xunit.
- Use descriptive PascalCase test names. Example: `HandshakeRoundTrips`.
- Use Arrange/Act/Assert structure.

### Comments and docs

- XML docs are rare. Add a terse one-line summary on public API or non-obvious
  private helpers.
- Inline comments use `//` and two spaces.
- Single-line action comments start with an imperative verb. No trailing
  period.

### What not to do

- No XML docs on every member.
- No block-scoped namespaces (`namespace X { }`).
- No `this.` qualification.
- No `is null` / `is not null` / `is { }` checks.
- No records on the `netstandard2.1` target.
- Do not disable nullable in new code.
- Do not introduce a new test framework.
- Never hand-edit generated code. See the codegen rules below.

### nsd codegen rules

- `.cs` files under `NsdRpc/CodeGen/Output/` are auto-generated. Never
  hand-edit them. Change the `.nsd` source instead.
- When you add, remove, or rename a field, edit the `.nsd` file, not the
  generated `.cs`.
- The output directory is untracked. Never commit it.

## Commit rules

Use terse, imperative, one-line commit messages.

- Start with an imperative verb, capitalized: `Fix`, `Add`, `Implement`,
  `Remove`, `Move`, `Update`, `Use`.
- Keep the rest lowercase and terse. Example: `Add rpc handshake message`.
- No prefixes (`feat:`/`fix:`), no scope parens, no emojis.
- No trailing period. Aim for under 60 characters.
- Be concrete. Name the specific thing changed.
- When a change closes a local issue, append its number:
  `Add rpc handshake message #1`.

## Config conventions

- Any new tunable is a config key, not a hardcoded constant.
- Document every key in a schema table, in the same change that adds it.
  When such tables exist, they live in `docs/specs/`.

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
