namespace NsdRpc;

// The dispatcher boundary. The peer invokes it from the dispatch worker with
// one inbound request or notification. The hook owns its error handling.
internal delegate ValueTask RpcDispatchHook(RpcContext context, byte[]? payload);
