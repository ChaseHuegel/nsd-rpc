# Chat (raw API)

A console chat app that exercises every RPC message kind over loopback TCP
using the bare API. Run it with:

```bash
dotnet run --project samples/Chat.Raw
```

## What it does

1. Starts a server and connects two clients: the interactive client and a
   scripted bot. Both live in this one process.
2. The interactive client sends a `ListUsersRequest`. The server streams each
   `User` back one at a time, 250 ms apart. The client prints each item as it
   arrives through an `IChunkSink<User>` callback, then prints the
   `ListUsersResponse` total.
3. The bot streams the same list through the `RpcStreamSession`
   `IAsyncEnumerable` wrapper, showing the second consumption path.
4. The bot sends a `ChatMessage` notification. The server relays it to every
   connected client, so both the interactive client and the bot print it.
5. The bot starts another list stream and cancels it after 800 ms. The client
   prints `Cancelled`, and the server drops the remaining chunks.
6. The input loop turns each line into a `ChatMessage` notification, which the
   server relays back to every client.
7. `/unknown` invokes a method id with no handler and prints the
   `MethodNotFound` result.
8. `/quit` or Ctrl+C closes the server and exits.

## What it exercises

- The TCP transport and handshake, over a loopback socket.
- A request with a streamed response (chunks plus a final response).
- Both stream consumption paths: the callback sink and the session wrapper.
- Cancellation: the client's token, the wire cancellation frame, and the
  server-side drop.
- Notifications in both directions, and the relay fan-out through
  `context.Host.Peers` in the server handler.
- The error model: a missing method returns `MethodNotFound` in the result.
