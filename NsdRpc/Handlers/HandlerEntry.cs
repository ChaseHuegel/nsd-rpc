namespace NsdRpc;

// One registered method. The registry holds the entries. The dispatcher runs
// the pipeline around ExecuteAsync.
internal abstract class HandlerEntry
{
    public abstract RpcDirection Direction { get; }

    public abstract ValueTask ExecuteAsync(
        RpcContext context, byte[]? payload, CancellationToken cancellationToken, RpcPeer peer);
}
