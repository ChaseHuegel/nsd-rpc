# Chat (generated)

The same chat app as [Chat.Raw](../Chat.Raw), driven through the
`RpcService` source generator instead of the bare API. Run it with:

```bash
dotnet run --project samples/Chat.Generated
```

## What is different from Chat.Raw

- One `[RpcService]` interface declares `ListUsersAsync` twice: as a
  `RpcStreamSession` stream and as an `IChunkSink` callback. The generator
  emits `ChatServiceClient` with both overloads, plus a `ListUsersAsyncHandler`
  base.
- The server handler is a subclass of the generated `ListUsersAsyncHandler`.
  The base carries the `RpcMethod` attribute, so the subclass registers
  without repeating the id.
- `ChatMessage` is a notification, declared as a `void NotifyChat(ChatMessage)`
  method. The generator emits the client call, a `NotifyChatHandler` base, and
  the relay and printer subclasses it. The relay rebroadcasts through the
  generated client's `RpcPeer` constructor.
- The project references `NsdRpc.Generators` as an analyzer
  (`OutputItemType="Analyzer"`), which is exactly how a consumer wires it.

## What it exercises

The same flow as Chat.Raw: a streamed user list, both consumption paths, a
cancelled stream, notifications relayed to every client, the
`MethodNotFound` path, and clean shutdown — with the call sites generated
instead of hand-written.
