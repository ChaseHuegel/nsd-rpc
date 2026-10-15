# Samples

Two console apps show the same chat pattern two ways. Each starts a server
on loopback TCP, connects two clients, streams a user list, cancels a
stream, and relays chat notifications between the clients.

| Sample | Client style |
| --- | --- |
| [Chat.Raw](Chat.Raw) | The bare API: manual invokes, streams, and notifications. |
| [Chat.Generated](Chat.Generated) | The `RpcService` source generator. |

Run one with:

```bash
dotnet run --project samples/Chat.Raw
```

Both compile their schemas with the `Needlefish.Generators` source generator.
No compiler install is needed.
