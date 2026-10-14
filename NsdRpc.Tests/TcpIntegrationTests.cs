using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using NsdRpc;
using NsdRpc.Tcp;
using NsdRpc.Wire;

namespace NsdRpc.Tests;

public sealed class TcpIntegrationTests
{
    [RpcMethod(300)]
    private sealed class EchoHandler : IRpcHandler<Handshake, Handshake>
    {
        public ValueTask<RpcResult<Handshake>> ExecuteAsync(
            Handshake request, RpcContext context, CancellationToken cancellationToken)
        {
            return new ValueTask<RpcResult<Handshake>>(
                RpcResult<Handshake>.Success(new Handshake(request.ProtocolVersion + 1)));
        }
    }

    [Fact]
    public async Task ClientConnectsAndCompletesHandshake()
    {
        var transport = new TcpServerTransport(new RpcOptions(), new TcpOptions());
        Task<IServerConnection?> accept = transport.AcceptAsync();
        var client = new TcpClientConnection(
            new RpcOptions(), new TcpOptions { Host = "127.0.0.1", Port = transport.Port });

        await client.ConnectAsync();

        IServerConnection? serverSide = await accept;
        Assert.NotNull(serverSide);
        Assert.Equal(1UL, serverSide.ClientId);
        Assert.True(client.IsConnected);
        Assert.True(serverSide.IsConnected);
    }

    [Fact]
    public async Task ServerAssignsSequentialClientIds()
    {
        var transport = new TcpServerTransport(new RpcOptions(), new TcpOptions());
        Task<IServerConnection?> first = transport.AcceptAsync();
        Task<IServerConnection?> second = transport.AcceptAsync();
        var firstClient = new TcpClientConnection(
            new RpcOptions(), new TcpOptions { Host = "127.0.0.1", Port = transport.Port });
        var secondClient = new TcpClientConnection(
            new RpcOptions(), new TcpOptions { Host = "127.0.0.1", Port = transport.Port });

        await firstClient.ConnectAsync();
        await secondClient.ConnectAsync();

        Assert.Equal(1UL, (await first)!.ClientId);
        Assert.Equal(2UL, (await second)!.ClientId);
    }

    [Fact]
    public async Task HandshakeVersionMismatchFailsConnect()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Task<Socket> accept = listener.AcceptSocketAsync();
            var client = new TcpClientConnection(
                new RpcOptions(), new TcpOptions { Host = "127.0.0.1", Port = port });
            Task connect = client.ConnectAsync();

            Socket server = await accept;
            byte[] header = new byte[4];
            await ReadExactAsync(server, header);
            int length = BinaryPrimitives.ReadInt32LittleEndian(header);
            byte[] payload = new byte[length];
            await ReadExactAsync(server, payload);
            Handshake received = Handshake.Deserialize((ReadOnlySpan<byte>)payload);
            Assert.Equal(1, received.ProtocolVersion);

            byte[] wrongVersion = new Handshake(2).Serialize();
            byte[] reply = new byte[4 + wrongVersion.Length];
            BinaryPrimitives.WriteInt32LittleEndian(reply, wrongVersion.Length);
            wrongVersion.CopyTo(reply, 4);
            await server.SendAsync(reply, SocketFlags.None);

            await Assert.ThrowsAsync<InvalidDataException>(() => connect);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task ClientServerRoundTripOverTcp()
    {
        var registry = new RpcRegistry();
        registry.Add(new EchoHandler());
        var transport = new TcpServerTransport(new RpcOptions(), new TcpOptions());
        var server = new RpcServer(transport, registry);
        server.Start();
        var connection = new TcpClientConnection(
            new RpcOptions(), new TcpOptions { Host = "127.0.0.1", Port = transport.Port });
        var client = new RpcClient(connection, new RpcRegistry());
        await connection.ConnectAsync();

        RpcResult<Handshake> result = await client.InvokeAsync<Handshake, Handshake>(300, new Handshake(41));

        Assert.True(result.IsSuccess, "code=" + result.ErrorCode + " message=" + result.ErrorMessage);
        Assert.Equal(42, result.Value.ProtocolVersion);
        server.Close();
    }

    [RpcMethod(999)]
    private sealed class DeferHandler : IRpcHandler<Handshake, Handshake>
    {
        public ValueTask<RpcResult<Handshake>> ExecuteAsync(
            Handshake request, RpcContext context, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<RpcResult<Handshake>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            return new ValueTask<RpcResult<Handshake>>(completion.Task);
        }
    }

    [Fact]
    public async Task AbruptServerCloseFaultsClientCalls()
    {
        var registry = new RpcRegistry();
        registry.Add(new DeferHandler());
        var transport = new TcpServerTransport(new RpcOptions(), new TcpOptions());
        var server = new RpcServer(transport, registry);
        server.Start();
        var connection = new TcpClientConnection(
            new RpcOptions(), new TcpOptions { Host = "127.0.0.1", Port = transport.Port });
        var client = new RpcClient(connection, new RpcRegistry());
        await connection.ConnectAsync();

        // The handler never responds, so the only completion is the disconnect.
        Task<RpcResult<Handshake>> call = client.InvokeAsync<Handshake, Handshake>(999, new Handshake(1));
        server.Close();

        RpcResult<Handshake> result = await call;
        Assert.False(result.IsSuccess);
        Assert.Equal((int)RpcErrorCode.Unavailable, result.ErrorCode);
    }

    private static async Task ReadExactAsync(Socket socket, byte[] buffer)
    {
        int filled = 0;
        while (filled < buffer.Length)
        {
            int read = await socket.ReceiveAsync(
                buffer.AsMemory(filled, buffer.Length - filled), SocketFlags.None);
            Assert.True(read > 0, "The socket closed before the expected bytes arrived.");
            filled += read;
        }
    }
}
