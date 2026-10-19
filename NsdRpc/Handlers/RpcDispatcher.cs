using System.Diagnostics.CodeAnalysis;

namespace NsdRpc;

// The dispatch pipeline. It sits between the peer's dispatch worker and the
// handlers. One dispatcher per peer over a frozen registry.
internal sealed class RpcDispatcher
{
    private readonly RpcRegistry _registry;
    private readonly RpcPeer _peer;

    public RpcDispatcher(RpcRegistry registry, RpcPeer peer)
    {
        _registry = registry;
        _peer = peer;
    }

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "The dispatch boundary catches every handler error. It is the one allowed catch-all.")]
    public async ValueTask DispatchAsync(RpcContext context, byte[]? payload)
    {
        if (!_registry.TryGet(context.Method, out HandlerEntry? entry)
            || !DirectionAllows(entry.Direction, context.IsServer))
        {
            FailRequest(context, (int)RpcErrorCode.MethodNotFound, "no handler for method " + context.Method);
            return;
        }

        CancellationTokenSource? cancelSource = null;
        if (context.RequestId != 0UL)
        {
            cancelSource = new CancellationTokenSource();
            if (!_peer.AttachCancellation(context.RequestId, cancelSource))
            {
                // The peer faulted or the call already completed.
                cancelSource.Cancel();
            }
        }

        try
        {
            await entry
                .ExecuteAsync(context, payload, cancelSource?.Token ?? CancellationToken.None, _peer)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            RpcLog.Error(_peer.LogSink, "handler failed", exception);
            FailRequest(context, (int)RpcErrorCode.InternalError, "internal error");
        }
        finally
        {
            cancelSource?.Dispose();
        }
    }

    private static bool DirectionAllows(RpcDirection direction, bool isServer)
    {
        if (direction == RpcDirection.Both)
        {
            return true;
        }

        if (direction == RpcDirection.ClientToServer)
        {
            return isServer;
        }

        return !isServer;
    }

    private void FailRequest(RpcContext context, int code, string message)
    {
        if (context.RequestId == 0UL)
        {
            if (RpcLog.IsEnabled(_peer.LogSink))
            {
                RpcLog.Drop(_peer.LogSink, "notification failed with " + code + ": " + message);
            }

            return;
        }

        _peer.TryFail(context.RequestId, code, message);
    }
}
