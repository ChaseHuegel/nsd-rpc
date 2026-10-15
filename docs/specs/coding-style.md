# Coding Style

One subject: the coding style rules for hand-written code in this repository.
The rules are Tiger Style and NASA's Power of 10, mapped to C#. Where this doc
conflicts with any other doc, this doc wins.

Sources:

- [Tiger Style](https://github.com/tigerbeetle/tigerbeetle/blob/main/docs/TIGER_STYLE.md)
- [The Power of 10](https://en.wikipedia.org/wiki/The_Power_of_10:_Rules_for_Developing_Safety-Critical_Code)

## Design goals

Safety first, then performance, then developer experience. Zero technical
debt. Do it right the first time.

## The Power of 10 in C#

1. Simple control flow. No `goto`. No recursion, direct or indirect. Use
   loops, or recursion with a hard depth limit.
2. Fixed loop upper bounds. Every loop must have a bound a reader can verify:
   a collection's length, a validated count, or an explicit constant. An
   unbounded loop, like an event loop, must assert its bound.
3. No heap allocation on hot paths. Allocate buffers and pools at startup.
   Hot paths use `Span<T>`, `stackalloc`, and `ArrayPool<T>`. No LINQ, no
   boxing, no closures in loops. Tests prove hot paths allocate nothing.
4. Short methods. Hard limit of 70 lines per method.
5. Dense assertions. Assert at least two properties per method on average.
   Assertions are compiled in every build. See the assertion rules below.
6. Smallest scope. Declare a variable at its first use. Minimize variables
   in scope. Mark as `readonly` or `const` when possible.
7. Check everything. Validate every parameter. Check every return value.
   Never discard a result.
8. Minimal preprocessor. No `#if` for behavior. Use runtime configuration or
   polymorphism.
9. No unsafe code. No `unsafe` blocks, raw pointers, or function pointers in
   hand-written code. The generated code is exempt. See the exemptions below.
10. Zero warnings. Every project compiles with `TreatWarningsAsErrors` and
    Roslyn analyzers at their strictest level. The CI build is the daily
    static analysis gate.

## Assertions

- Assertions catch programmer errors. Expected errors are handled, not
  asserted. Assertion failures crash. They turn catastrophic correctness bugs
  into liveness bugs.
- Public API validates arguments with guard clauses that throw. Example:
  `ArgumentNullException.ThrowIfNull`.
- Internal invariants use an always-on assert helper. The helper throws on
  failure in every build. Debug-only asserts do not qualify. The helper does
  not exist yet. The first library code change must add it.
- Assert the positive space you expect and the negative space you do not
  expect.
- Pair assertions. Assert a property right before you write data and right
  after you read it back.
- Split compound assertions. Write two asserts instead of one compound
  condition.
- Assert relationships between compile-time constants.
- Assertions are the fuzzing oracle. Fuzzing finds the bugs your assertions
  describe.

## Control flow

- Split compound conditions into nested `if` and `else`. Split complex
  `else if` chains into `else { if { } }` trees.
- State invariants positively. Write `if (index < count)`. Avoid
  `if (index >= count)`.
- Centralize control flow in one function. Push `if`s up and `for`s down.
  Leaf helpers stay pure and branch-free.
- Extract hot loops into static functions with primitive arguments, without
  `this`.

## Memory and limits

- Put a limit on everything. Queues, buffers, and counts get explicit upper
  bounds. Limits are config keys, never magic constants.
- Use explicit-width types. Use `int`, `uint`, `long`, `ulong`. Avoid
  `nuint` and `nint` in wire-format code.
- Pass value types of 16 bytes or less by value. Pass larger ones with `in`
  or `ref`.
- Build into spans and out parameters. Avoid intermediate copies on hot
  paths.
- Zero buffers before reuse. Zero sensitive data before release. Buffer
  bleeds leak data.
- Treat `index`, `count`, and `size` as distinct concepts. Indexes are
  0-based. Counts are 1-based. Sizes multiply by the unit. Name the unit.
- Make division and rounding intent explicit. Use `checked` arithmetic in
  wire-format math.

## Naming

- No abbreviations. Spell names out.
- Prefer nouns over adjectives for things. Example: `pipeline`, not
  `preparing`.
- Give related names the same length. Example: `source` and `target`, not
  `src` and `dest`.
- Add units or qualifiers last, sorted by descending significance. Example:
  `latencyMsMax` lines up with `latencyMsMin`.
- Do not overload a name with two meanings.
- Prefix a helper with its caller when the call history helps. Example:
  `ReadSector` and `ReadSectorCallback`.
- Put callbacks and cancellation tokens last in parameter lists.

C# conventions from the original style stay: PascalCase for types and
methods, `_camelCase` for private fields, camelCase for parameters and
locals, `UPPER_SNAKE_CASE` for private constants. File-scoped namespaces.
No `this.` qualification. `== null` and `!= null`, not `is null`.

## Layout

- Four-space indentation.
- Hard limit of 100 columns per line. No exceptions.
- Allman braces. The opening brace sits on its own line.
- A single-line `if` may omit braces. Example: `if (index == count) break;`.
  Multi-line statements always have braces.
- Single-line accessors and lambdas use expression bodies.
- One type per file.
- Order in a type: constants, fields, constructors, public methods, private
  helpers.

## Comments

- Comments are sentences. Capital letter and full stop. One space after the
  slashes.
- Say why. A comment explains the reason for the decision.
- Trailing-line comments may be phrases without punctuation.
- XML docs stay rare. Put a terse one-line summary on public API.

## Types

- Structs for small data holders and value types. Classes for services and
  long-lived objects. `readonly struct` for immutable values.
- Prefer the most concrete type. Prefer `T[]`, `List<T>`, and
  `IReadOnlyList<T>` over `IEnumerable<T>` as declared types.
- Never expose a mutable collection.
- No records on the netstandard2.1 target.
- Prefer `internal`. Make types `public` only for a real cross-assembly
  need.
- Mark leaf classes `sealed`. Mark fields set once `readonly`.
- Prefer primary constructors for small types.
- Prefer collection expressions (`[]`).
- Use named arguments when a value's meaning is not self-evident.
- Use `var` only when the type is visible on the same line. Otherwise write
  the type.

## Exceptions and results

- Expected failures are results, not exceptions. Return a `Result<T>` or an
  error enum. Reserve exceptions for programmer errors.
- Handle every error. A study of production failures found that 92 percent
  of catastrophic failures came from mishandled non-fatal errors. Test every
  error path.
- Use guard clauses with `?? throw` or a plain `throw` at the top.
- Use one try/catch at module boundaries, for logging and graceful
  degradation.

## Null handling

- Use `== null` and `!= null` for checks.
- Use `?.` for null-conditional calls on delegates and events.
- Use `??` and `??=` for coalescing.
- Use `?? throw` for guard clauses in constructors.

## Signatures

- Prefer the simplest return type that is correct. `void` beats `bool`.
  `bool` beats `T`. `T` beats `T?`. `T?` beats a result type.
- Preconditions hold for the life of the method. Avoid `await` between a
  check and its use.

## Performance

- Sketch the cost of a design against network, disk, memory, and CPU before
  you write it. Optimize the slowest resource first, after frequency of use.
- Separate control plane from data plane. Batch work.
- Be explicit. Do not rely on the compiler to prove it for you.

## Exemptions

- The generated wire types are not hand-written. Style rules do not apply.
  Unsafe is allowed there. The library keeps `AllowUnsafeBlocks` for them. See
  [NsdRpc/NsdRpc.csproj:9](../../NsdRpc/NsdRpc.csproj).
- Never edit generated files. Change the schema. See
  [nsd-schemas](nsd-schemas.md).
- Compiler output defects are tracked as issues, not patched by hand. See
  [issue 0001](../issues/0001-malformed-buffer-deserialize-overread.md).

## Enforcement

- `TreatWarningsAsErrors`, `EnableNETAnalyzers`, and `AnalysisLevel` set to
  `latest` in every project. See
  [NsdRpc/NsdRpc.csproj:15-17](../../NsdRpc/NsdRpc.csproj) and
  [NsdRpc.Tests/NsdRpc.Tests.csproj:14-16](../../NsdRpc.Tests/NsdRpc.Tests.csproj).
- No lint scripts. `dotnet build` is the check.
- Method length, assertion density, and loop bounds are review rules. The
  build cannot measure them.

## Conflicts resolved

These rules replace the original conventions where they disagreed:

| Old rule | New rule |
|---|---|
| All blocks must have braces | A single-line `if` may omit braces |
| Abbreviations like `ID` and `IO` allowed | No abbreviations |
| `in` on all value-type parameters | `in`/`ref` only above 16 bytes |
| Comments start with an imperative verb, no period | Comments are sentences with a full stop |
| Terse one-line commits under 60 characters | Descriptive commits with a body |
| `var` when obvious | `var` only when the type is visible on the same line |
| Debug-only asserts | Always-on asserts in every build |
