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
- The project references `NsdRpc.Generators` as an analyzer
  (`OutputItemType="Analyzer"`), which is exactly how a consumer wires it.

## What stays raw

`ChatMessage` is a notification, and the generator has no notification shape
yet. It is sent with `RpcClient.Notify` and handled by a plain `IRpcHandler`
on both sides. See
[issue 0016](../../docs/issues/0016-generator-notification-shape.md).

## What it exercises

The same flow as Chat.Raw: a streamed user list, both consumption paths, a
cancelled stream, notifications relayed to every client, the
`MethodNotFound` path, and clean shutdown — with the call sites generated
instead of hand-written.
