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

Both need `nsdc` on the PATH. See
[docs/specs/codegen-pipeline.md](../docs/specs/codegen-pipeline.md).
