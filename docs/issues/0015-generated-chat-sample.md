# Improvement: Add the generated chat sample

- Type: improvement
- Status: open
- Workflow: ../specs/issues.md

## Problem

The raw chat sample uses the bare API. A second sample of the same pattern
shows the source generator in action: the typed client, the handler bases,
and the analyzer project reference a consumer wires.

## Acceptance criteria

- [ ] `samples/Chat.Generated` is a console app that mirrors the raw sample's flow.
- [ ] The project references `NsdRpc.Generators` as an analyzer, so the sample doubles as the consumer-setup example.
- [ ] A `[RpcService]` interface declares `ListUsersAsync` as a `RpcStreamSession<User, ListUsersResponse>`.
- [ ] The sample uses the generated `ChatServiceClient` and `ListUsersAsyncHandler` base.
- [ ] Chat remains a notification, which the generator does not express, so it uses raw `Notify` and a raw handler on both sides. The README notes this and points at issue 0016.
- [ ] The sample joins `NsdRpc.sln`, builds in CI, and is not packed.
- [ ] A README explains how to run the sample and what each step exercises.
