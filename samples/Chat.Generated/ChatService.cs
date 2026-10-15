using Chat;
using NsdRpc;

// One interface declares both directions. Invocation shapes (no RpcContext)
// generate ChatClient; handler shapes (with RpcContext) generate the
// ChatHandler base you subclass.
[RpcService]
public interface IChat
{
    // Client invocations.
    [RpcMethod(Methods.ListUsers)]
    RpcStreamSession<User, ListUsersResponse> ListUsersAsync(
        ListUsersRequest request, CancellationToken cancellationToken = default);

    [RpcMethod(Methods.ListUsers)]
    Task<RpcResult<ListUsersResponse>> ListUsersAsync(
        ListUsersRequest request, IChunkSink<User> sink, CancellationToken cancellationToken = default);

    [RpcMethod(Methods.ChatMessage)]
    void NotifyChat(ChatMessage message);

    // Server handlers.
    [RpcMethod(Methods.ListUsers, Streams = typeof(User))]
    ValueTask<RpcResult<ListUsersResponse>> ListUsersAsync(
        ListUsersRequest request, RpcContext context, CancellationToken cancellationToken);

    [RpcMethod(Methods.ChatMessage)]
    void OnNotifyChat(ChatMessage message, RpcContext context, CancellationToken cancellationToken);
}
