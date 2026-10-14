using System.Collections.Concurrent;
using Chat;
using NsdRpc;
using NsdRpc.Tcp;

return await RunAsync();

static async Task<int> RunAsync()
{
    // The server tracks its clients so the chat handler can relay to everyone.
    var peers = new ConcurrentDictionary<ulong, RpcPeer>();
    var registry = new RpcRegistry();
    registry.Add(new ListUsersHandler());
    registry.Add(new ChatRelayHandler(peers));
    registry.Freeze();

    var transport = new TcpServerTransport(new RpcOptions(), new TcpOptions());
    var server = new RpcServer(transport, registry);
    server.ClientConnected += peer =>
    {
        peers[peer.ClientId] = peer;
        Console.WriteLine($"client {peer.ClientId} connected");
    };
    server.ClientDisconnected += peer =>
    {
        peers.TryRemove(peer.ClientId, out _);
        Console.WriteLine($"client {peer.ClientId} disconnected");
    };
    server.Start();

    RpcClient local = await ConnectAsync(transport.Port, string.Empty);
    RpcClient bot = await ConnectAsync(transport.Port, "[bot] ");

    // The local client streams the user list through a callback sink. Each
    // item prints as it arrives, 250 ms apart.
    Console.WriteLine("users:");
    RpcResult<ListUsersResponse> list = await local.InvokeStreamAsync<ListUsersRequest, User, ListUsersResponse>(
        Methods.ListUsers, new ListUsersRequest(0), new UserSink());
    Console.WriteLine(list.IsSuccess ? $"total: {list.Value.Total}" : $"list failed: {(RpcErrorCode)list.ErrorCode}");

    // The bot streams through the session wrapper, then greets everyone.
    RpcStreamSession<User, ListUsersResponse> botList = bot.InvokeStreamAsync<ListUsersRequest, User, ListUsersResponse>(
        Methods.ListUsers, new ListUsersRequest(0));
    await foreach (User user in botList.Chunks)
    {
        Console.WriteLine($"[bot]   {user.Id}: {user.Name}");
    }

    RpcResult<ListUsersResponse> botResult = await botList.Response;
    if (botResult.IsSuccess)
    {
        Console.WriteLine($"[bot] total: {botResult.Value.Total}");
    }

    bot.Notify(Methods.ChatMessage, new ChatMessage("bot", "hello everyone"));

    // The bot cancels a second stream partway. The client gets Cancelled and
    // the server drops the remaining chunks and the response.
    Console.WriteLine("[bot] cancelling a stream partway...");
    using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(800));
    RpcResult<ListUsersResponse> cancelled = await bot.InvokeStreamAsync<ListUsersRequest, User, ListUsersResponse>(
        Methods.ListUsers, new ListUsersRequest(0), new UserSink("[bot] "), cancellationToken: cancel.Token);
    Console.WriteLine($"[bot] cancelled stream: {(RpcErrorCode)cancelled.ErrorCode}");

    // Chat loop. Every line becomes a notification the server relays back to
    // every client, including the sender.
    Console.WriteLine("type a message, /unknown for a missing method, /quit to exit");
    Console.CancelKeyPress += (_, eventArgs) => eventArgs.Cancel = true;
    while (true)
    {
        string? line = Console.ReadLine();
        if (line == null || line == "/quit")
        {
            break;
        }

        if (line == "/unknown")
        {
            RpcResult<User> missing = await local.InvokeAsync<User, User>(999UL, new User(0, ""));
            Console.WriteLine($"unknown method: {(RpcErrorCode)missing.ErrorCode}");
            continue;
        }

        if (line.Length > 0)
        {
            local.Notify(Methods.ChatMessage, new ChatMessage("me", line));
        }
    }

    server.Close();
    return 0;
}

static async Task<RpcClient> ConnectAsync(int port, string prefix)
{
    var connection = new TcpClientConnection(new RpcOptions(), new TcpOptions { Host = "127.0.0.1", Port = port });
    await connection.ConnectAsync();
    var registry = new RpcRegistry();
    registry.Add(new ChatPrinter(prefix));
    registry.Freeze();
    return new RpcClient(connection, registry);
}

[RpcMethod(Methods.ListUsers, RpcDirection.ClientToServer)]
public sealed class ListUsersHandler : IRpcHandler<ListUsersRequest, ListUsersResponse>
{
    public async ValueTask<RpcResult<ListUsersResponse>> ExecuteAsync(
        ListUsersRequest request, RpcContext context, CancellationToken cancellationToken)
    {
        int count = request.MaxItems > 0 ? Math.Min(request.MaxItems, FakeUsers.All.Count) : FakeUsers.All.Count;
        for (int i = 0; i < count; i++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
            context.Stream(FakeUsers.All[i]);
        }

        return RpcResult<ListUsersResponse>.Success(new ListUsersResponse(count));
    }
}

// A notification handler. The response type is unused because nothing is sent
// back for a notification.
[RpcMethod(Methods.ChatMessage)]
public sealed class ChatRelayHandler(ConcurrentDictionary<ulong, RpcPeer> peers)
    : IRpcHandler<ChatMessage, ListUsersResponse>
{
    public ValueTask<RpcResult<ListUsersResponse>> ExecuteAsync(
        ChatMessage message, RpcContext context, CancellationToken cancellationToken)
    {
        foreach (RpcPeer peer in peers.Values)
        {
            peer.Notify(Methods.ChatMessage, in message);
        }

        return new ValueTask<RpcResult<ListUsersResponse>>(RpcResult<ListUsersResponse>.Success(default));
    }
}

[RpcMethod(Methods.ChatMessage)]
public sealed class ChatPrinter(string prefix) : IRpcHandler<ChatMessage, ListUsersResponse>
{
    public ValueTask<RpcResult<ListUsersResponse>> ExecuteAsync(
        ChatMessage message, RpcContext context, CancellationToken cancellationToken)
    {
        Console.WriteLine(prefix + message.Author + ": " + message.Text);
        return new ValueTask<RpcResult<ListUsersResponse>>(RpcResult<ListUsersResponse>.Success(default));
    }
}

public sealed class UserSink(string prefix = "") : IChunkSink<User>
{
    public void OnChunk(in User user)
    {
        Console.WriteLine(prefix + "  " + user.Id + ": " + user.Name);
    }
}
