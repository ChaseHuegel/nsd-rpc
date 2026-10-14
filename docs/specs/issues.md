# Issue Format and Workflow

One subject: how to track work in this repository. The index lives at
[docs/issues/index.md](../issues/index.md).

## Index structure

The index has two sections: Active Sprint and Backlog. Each section has three
type subsections: Bug, Improvement, and Research.

Each issue is one checkbox bullet that links to its own document:

```
- [ ] [Title](/docs/issues/0000-slug.md) — one-line summary
```

- An open issue is unchecked (`- [ ]`).
- A done issue is checked (`- [x]`) and stays in place.
- Sprint rotation and backlog order are user-managed. Never archive, roll, or
  rotate sprints.

## Issue documents

One loose markdown document per issue lives in `docs/issues/`. Name each file
`0000-short-slug.md` with a zero-padded, ascending number.

Every issue document uses this template:

```
# <Type>: <Title>

- Type: bug | improvement | research
- Status: open | in-progress | done
- Workflow: ../specs/issues.md

## Problem

Describe the current behavior and why it is wrong. For research, state the
question to answer.

## Acceptance criteria

- [ ] Describe a verifiable outcome.
- [ ] Describe the second verifiable outcome.
```

## Adding an issue

1. Create the issue document in `docs/issues/`.
2. Add an unchecked `- [ ]` entry under the matching section and type
   subsection in `docs/issues/index.md`.

## Working an issue

Work one issue at a time through this lifecycle:

1. Read and understand the issue.
2. Research and explore the issue when it is complex or vague.
3. Form a development plan.
4. Self-review the plan before implementation.
5. Implement the change.
6. Self-review the implemented change.
7. Mark the issue complete.
8. Commit the change.

Follow the standards in [development.md](../development.md) and the quality
gates in [AGENTS.md](../../AGENTS.md). Run the docs pass before finalizing.

## Adjacent problems

A problem found during development that the issue does not require is an
adjacent problem. Do not fix it. Report it to the user and capture it as a new
issue in the Backlog. Fix only what the issue requires.

## Marking an issue complete

1. Set the issue document status to `done`.
2. Check the box (`- [x]`) for that issue in `docs/issues/index.md`.
3. Keep the issue in its subsection.

## Commands

- Work the active sprint: address every unchecked item in Active Sprint, in
  order, until none remains.
- Work the next issue: find the next unchecked item in Active Sprint.
- Work the next bug: find the next unchecked item of the named type in Active
  Sprint. The named type is one of bug, improvement, or research. When no
  unchecked item of that type exists, report that none remains.
