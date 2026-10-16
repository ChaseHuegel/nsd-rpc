# Chat (generated)

The same chat app as [Chat.Raw](../Chat.Raw), driven through the
`RpcService` source generator instead of the bare API. Run it with:

```bash
dotnet run --project samples/Chat.Generated
```

## What is different from Chat.Raw

- One `[RpcService]` interface, `IChat`, declares both directions. The
  invocation shapes (no `RpcContext`) generate `ChatClient`. The handler
  shapes (with `RpcContext`) generate the `ChatHandler` base.
- The server subclasses `ChatHandler` to stream the user list and relay chat,
  and registers it with `RpcRegistry.AddService`.
- The client subclasses `ChatHandler` too, but overrides only `OnNotifyChat`
  to print incoming messages — a partial implementation of the same base.
- The relay fans out through `new ChatClient(peer)` per peer in
  `context.Host.Peers`, using the generated client's `RpcPeer` constructor.
- The project references `NsdRpc.Generators` as an analyzer
  (`OutputItemType="Analyzer"`), which is exactly how a consumer wires it.

## What it exercises

The same flow as Chat.Raw: a streamed user list, both consumption paths, a
cancelled stream, notifications relayed to every client, the
`MethodNotFound` path, and clean shutdown — with both the invokable and
handling halves generated from one interface.
