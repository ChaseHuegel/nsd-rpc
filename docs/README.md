# Docs

This is the subject index for the repository. It is the recommended first read.
Fetch only the doc you need. Never fetch the whole tree.

| Subject | Doc |
|---|---|
| How to work in this repo: toolchain, build, commits | [development](development.md) |
| Coding style: Tiger Style and the Power of 10 in C# | [specs/coding-style](specs/coding-style.md) |
| Testing doctrine: edge cases, determinism, fuzzing | [testing](testing.md) |
| As-built architecture and module map | [architecture](architecture.md) |
| nsd schemas and the wire format | [specs/nsd-schemas](specs/nsd-schemas.md) |
| RPC wire protocol and envelope | [specs/rpc-protocol](specs/rpc-protocol.md) |
| RPC engine and handler API | [specs/rpc-api](specs/rpc-api.md) |
| The nsdc build pipeline | [specs/codegen-pipeline](specs/codegen-pipeline.md) |
| Issue format and workflow | [specs/issues](specs/issues.md) |

## Live index

The issue tracker is a local index at [docs/issues/index.md](issues/index.md).
Work the Active Sprint items there. Read [specs/issues.md](specs/issues.md)
before you add or work an issue.

## Rules

- One subject per doc. Link instead of copying. Never duplicate content.
- Source of truth is the code. Docs record behavior and point at `file:line`.
- Every change set ends with a docs pass. Stale docs are a bug.
- All new doc prose uses Simplified Technical English.
