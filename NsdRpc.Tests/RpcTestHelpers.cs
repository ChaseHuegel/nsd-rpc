using System.Buffers.Binary;
using NsdRpc;
using NsdRpc.Wire;

namespace NsdRpc.Tests;

internal static class RpcTestHelpers
{
    public static void Pump(InMemoryConnection from, RpcPeer into)
    {
        while (from.TryDequeueInbound(out byte[] frame))
        {
            into.HandleFrame(frame);
        }
    }

    public static void Pump(InMemoryConnection from, RpcClient into)
    {
        while (from.TryDequeueInbound(out byte[] frame))
        {
            into.HandleFrame(frame);
        }
    }

    // Frames one envelope and delivers it directly to a peer's HandleFrame.
    public static void Deliver(RpcPeer peer, in Envelope envelope)
    {
        int size = envelope.GetSize();
        byte[] frame = new byte[4 + size];
        BinaryPrimitives.WriteInt32LittleEndian(frame, size);
        envelope.SerializeInto(frame.AsSpan(4, size));
        peer.HandleFrame(frame);
    }

    // Wires a dispatcher that signals the gate when one dispatch completes.
    // The response frame is sent before the gate fires, so the pump after the
    // gate is deterministic. Reattach before every round trip for a fresh gate.
    public static TaskCompletionSource AttachDispatcher(RpcPeer peer, RpcRegistry registry)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = new RpcDispatcher(registry, peer);
        peer.DispatchHook = async (context, payload) =>
        {
            await dispatcher.DispatchAsync(context, payload).ConfigureAwait(false);
            gate.TrySetResult();
        };
        return gate;
    }

    public static async Task<RpcResult<Handshake>> RoundTrip(
        InMemoryConnection clientConn,
        InMemoryConnection serverConn,
        RpcPeer client,
        RpcPeer server,
        TaskCompletionSource gate,
        ulong method,
        Handshake request)
    {
        Task<RpcResult<Handshake>> call = client.InvokeAsync<Handshake, Handshake>(method, request);
        Pump(serverConn, server);
        await gate.Task;
        Pump(clientConn, client);
        return await call;
    }

    public static async Task<RpcResult<Handshake>> RoundTrip(
        InMemoryConnection clientConn,
        InMemoryConnection serverConn,
        RpcClient client,
        RpcPeer server,
        TaskCompletionSource gate,
        ulong method,
        Handshake request)
    {
        Task<RpcResult<Handshake>> call = client.InvokeAsync<Handshake, Handshake>(method, request);
        Pump(serverConn, server);
        await gate.Task;
        Pump(clientConn, client);
        return await call;
    }
}
