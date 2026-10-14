# Improvement: Add the raw chat sample

- Type: improvement
- Status: done
- Workflow: ../specs/issues.md

## Problem

The library had no runnable sample. A console chat app over loopback TCP
exercises the public API end to end: requests, streamed responses, and
notifications in both directions. Two samples share one pattern. This
issue adds the shared contract and the raw-API sample.

## Acceptance criteria

- [x] `samples/Chat.Common` holds the shared `Chat.nsd` schema (`User`, `ListUsersRequest`, `ListUsersResponse`, `ChatMessage`), the method id constants, and the fake user store.
- [x] `Chat.Common` wires the `NsdcCodegen` build target in its csproj, the same way an external nsd consumer does.
- [x] `samples/Chat.Raw` is a console app that starts a server and connects an interactive client and a bot client over loopback TCP.
- [x] The interactive client streams the user list through `IChunkSink<User>` and prints each item as it arrives.
- [x] The bot client streams through `RpcStreamSession` and cancels a second stream partway, printing the `Cancelled` result.
- [x] Each input line becomes a `ChatMessage` notification. The server rebroadcasts it to every client through the 0013 events. Both clients print incoming messages.
- [x] A `/unknown` command prints the `MethodNotFound` result.
- [x] `/quit` or Ctrl+C shuts down cleanly.
- [x] Each sample uses only the public API. Both join `NsdRpc.sln` and build in CI without packing.
- [x] A README explains how to run the sample and what each step exercises.
