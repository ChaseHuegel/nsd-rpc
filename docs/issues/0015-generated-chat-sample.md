# Improvement: Add the generated chat sample

- Type: improvement
- Status: done
- Workflow: ../specs/issues.md

## Problem

The raw chat sample used the bare API. A second sample of the same pattern
shows the source generator in action: the typed client, the handler bases,
and the analyzer project reference a consumer wires.

## Acceptance criteria

- [x] `samples/Chat.Generated` is a console app that mirrors the raw sample's flow.
- [x] The project references `NsdRpc.Generators` as an analyzer, so the sample doubles as the consumer-setup example.
- [x] A `[RpcService]` interface declares `ListUsersAsync` as a `RpcStreamSession<User, ListUsersResponse>` and as a sink overload.
- [x] The sample uses the generated `ChatServiceClient` and `ListUsersAsyncHandler` base.
- [x] Chat remains a notification, which the generator does not express, so it uses raw `Notify` and a raw handler on both sides. The README notes this and points at issue 0016.
- [x] The sample joins `NsdRpc.sln`, builds in CI, and is not packed.
- [x] A README explains how to run the sample and what each step exercises.
