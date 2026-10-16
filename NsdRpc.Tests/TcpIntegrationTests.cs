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
        var firstClient = new TcpClientConnection(
            new RpcOptions(), new TcpOptions { Host = "127.0.0.1", Port = transport.Port });

        await firstClient.ConnectAsync();
        IServerConnection firstConnection = (await first)!;

        // The second accept starts after the first handshake, so connection
        // order is not a scheduling coincidence.
        Task<IServerConnection?> second = transport.AcceptAsync();
        var secondClient = new TcpClientConnection(
            new RpcOptions(), new TcpOptions { Host = "127.0.0.1", Port = transport.Port });
        await secondClient.ConnectAsync();

        Assert.Equal(1UL, firstConnection.ClientId);
        Assert.Equal(2UL, (await second)!.ClientId);
    }

    [Fact]
    public async Task TransportCloseMakesAcceptReturnNull()
    {
        var transport = new TcpServerTransport(new RpcOptions(), new TcpOptions());
        Task<IServerConnection?> accept = transport.AcceptAsync();

        transport.Close();

        Assert.Null(await accept.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task TransportSkipsAConnectionWithABadHandshake()
    {
        var transport = new TcpServerTransport(new RpcOptions(), new TcpOptions());
        Task<IServerConnection?> accept = transport.AcceptAsync();

        // The socket stays open until the end of the test, so the server reads
        // the bad handshake from a live connection.
        using var bad = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await bad.ConnectAsync(IPAddress.Loopback, transport.Port);
        await SendFrameAsync(bad, new Handshake(2).Serialize());

        var good = new TcpClientConnection(
            new RpcOptions(), new TcpOptions { Host = "127.0.0.1", Port = transport.Port });
        await good.ConnectAsync();

        IServerConnection? serverSide = await accept.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(serverSide);
        Assert.Equal(2UL, serverSide.ClientId);
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

            using Socket server = await accept;
            byte[] payload = await ReadFrameAsync(server);
            Handshake received = Handshake.Deserialize((ReadOnlySpan<byte>)payload);
            Assert.Equal(1, received.ProtocolVersion);

            await SendFrameAsync(server, new Handshake(2).Serialize());

            await Assert.ThrowsAsync<InvalidDataException>(
                () => connect.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task ClientFailsConnectOnBadHandshakeLength()
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

            using Socket server = await accept;
            await ReadFrameAsync(server);

            // A zero length fails the handshake frame check.
            await server.SendAsync(new byte[4], SocketFlags.None);

            await Assert.ThrowsAsync<InvalidDataException>(
                () => connect.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task ClientFailsConnectWhenTheServerCloses()
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
            await ReadFrameAsync(server);
            server.Dispose();

            await Assert.ThrowsAsync<InvalidDataException>(
                () => connect.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task DecoderRejectionDisconnectsTheServerPeer()
    {
        var transport = new TcpServerTransport(new RpcOptions(), new TcpOptions());
        var server = new RpcServer(transport, new RpcRegistry());
        RpcPeer? connected = null;
        RpcPeer? disconnected = null;
        var connectedGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnectedGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.ClientConnected += peer =>
        {
            connected = peer;
            connectedGate.TrySetResult();
        };
        server.ClientDisconnected += peer =>
        {
            disconnected = peer;
            disconnectedGate.TrySetResult();
        };
        server.Start();

        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(IPAddress.Loopback, transport.Port);
        await ReadFrameAsync(socket);
        await SendFrameAsync(socket, new Handshake(1).Serialize());
        await connectedGate.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // A zero-length frame fails the decoder. The receive loop ends and
        // fires the disconnect.
        await socket.SendAsync(new byte[4], SocketFlags.None);
        await disconnectedGate.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.NotNull(connected);
        Assert.Same(connected, disconnected);
        Assert.Null(server.GetPeer(connected.ClientId));
        server.Close();
    }

    [Fact]
    public async Task ClientRejectsFrameAboveTheSendQueueBound()
    {
        var transport = new TcpServerTransport(new RpcOptions(), new TcpOptions());
        Task<IServerConnection?> accept = transport.AcceptAsync();
        var client = new TcpClientConnection(
            new RpcOptions(),
            new TcpOptions { Host = "127.0.0.1", Port = transport.Port, SendQueueBytes = 32 });
        await client.ConnectAsync();
        await accept;

        var envelope = new Envelope(1, (byte)RpcMessageKind.Notification, 0UL, 1UL, null, null);
        byte[] frame = new byte[4 + envelope.GetSize()];
        BinaryPrimitives.WriteInt32LittleEndian(frame, envelope.GetSize());
        envelope.SerializeInto(frame.AsSpan(4));

        Assert.True(client.TrySend(frame));
        Assert.False(client.TrySend(new byte[33]));
    }

    [Fact]
    public async Task ClientRejectsASecondConnect()
    {
        var transport = new TcpServerTransport(new RpcOptions(), new TcpOptions());
        Task<IServerConnection?> accept = transport.AcceptAsync();
        var client = new TcpClientConnection(
            new RpcOptions(), new TcpOptions { Host = "127.0.0.1", Port = transport.Port });
        await client.ConnectAsync();
        await accept;

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConnectAsync());
    }

    [Fact]
    public async Task ClientRejectsSendAfterTheConnectionCloses()
    {
        var transport = new TcpServerTransport(new RpcOptions(), new TcpOptions());
        Task<IServerConnection?> accept = transport.AcceptAsync();
        var client = new TcpClientConnection(
            new RpcOptions(), new TcpOptions { Host = "127.0.0.1", Port = transport.Port });
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Disconnected += () => disconnected.TrySetResult();
        await client.ConnectAsync();
        await accept;

        transport.Close();
        await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(client.IsConnected);
        Assert.False(client.TrySend(new byte[4]));
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

    [Fact]
    public async Task DisconnectedClientLeavesTheAcceptedList()
    {
        var transport = new TcpServerTransport(new RpcOptions(), new TcpOptions());
        Task<IServerConnection?> accept = transport.AcceptAsync();
        using Socket socket = await ConnectRawAsync(transport);
        IServerConnection serverSide = (await accept)!;
        Assert.Equal(1, transport.AcceptedConnectionCount);

        var removed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        serverSide.Disconnected += () => removed.TrySetResult();
        socket.Dispose();

        await removed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, transport.AcceptedConnectionCount);
        transport.Close();
    }

    [Fact]
    public async Task RepeatedConnectAndDisconnectKeepsTheAcceptedListBounded()
    {
        var transport = new TcpServerTransport(new RpcOptions(), new TcpOptions());
        for (int cycle = 0; cycle < 5; cycle++)
        {
            Task<IServerConnection?> accept = transport.AcceptAsync();
            using Socket socket = await ConnectRawAsync(transport);
            IServerConnection serverSide = (await accept)!;
            var removed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            serverSide.Disconnected += () => removed.TrySetResult();
            socket.Dispose();
            await removed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        Assert.Equal(0, transport.AcceptedConnectionCount);
        transport.Close();
    }

    [Fact]
    public async Task CloseRemovesAcceptedConnections()
    {
        var transport = new TcpServerTransport(new RpcOptions(), new TcpOptions());
        Task<IServerConnection?> accept = transport.AcceptAsync();
        using Socket socket = await ConnectRawAsync(transport);
        _ = await accept;
        Assert.Equal(1, transport.AcceptedConnectionCount);

        transport.Close();

        Assert.Equal(0, transport.AcceptedConnectionCount);
    }

    // A raw client socket that finishes the handshake and then disconnects on
    // Dispose. It exercises the accepted-list removal without an RpcClient.
    private static async Task<Socket> ConnectRawAsync(TcpServerTransport transport)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(IPAddress.Loopback, transport.Port);
        await ReadFrameAsync(socket);
        await SendFrameAsync(socket, new Handshake(1).Serialize());
        return socket;
    }

    private static async Task<byte[]> ReadFrameAsync(Socket socket)
    {
        byte[] header = new byte[4];
        await ReadExactAsync(socket, header);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        byte[] payload = new byte[length];
        await ReadExactAsync(socket, payload);
        return payload;
    }

    private static async Task SendFrameAsync(Socket socket, byte[] payload)
    {
        byte[] frame = new byte[4 + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, payload.Length);
        payload.CopyTo(frame, 4);
        await socket.SendAsync(frame, SocketFlags.None);
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
