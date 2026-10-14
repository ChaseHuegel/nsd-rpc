using Chat;
using NsdRpc;

// One interface drives both sides. The generator emits ChatServiceClient for
// the client and a ListUsersAsyncHandler base for the server.
[RpcService]
public interface IChatService
{
    [RpcMethod(Methods.ListUsers, RpcDirection.ClientToServer)]
    RpcStreamSession<User, ListUsersResponse> ListUsersAsync(
        ListUsersRequest request, CancellationToken cancellationToken = default);

    [RpcMethod(Methods.ListUsers, RpcDirection.ClientToServer)]
    Task<RpcResult<ListUsersResponse>> ListUsersAsync(
        ListUsersRequest request, IChunkSink<User> sink, CancellationToken cancellationToken = default);
}
