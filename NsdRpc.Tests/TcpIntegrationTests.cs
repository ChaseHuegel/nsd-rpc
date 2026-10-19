using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using NsdRpc;
using NsdRpc.Tcp;
using NsdRpc.Tests.Wire;
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

    private sealed class BlobEchoHandler : IRpcHandler<Blob, Blob>
    {
        public ValueTask<RpcResult<Blob>> ExecuteAsync(
            Blob request, RpcContext context, CancellationToken cancellationToken)
        {
            return new ValueTask<RpcResult<Blob>>(RpcResult<Blob>.Success(request));
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
    public async Task SilentClientDoesNotBlockACallFromTheNextClient()
    {
        var transport = new TcpServerTransport(
            new RpcOptions(),
            new TcpOptions { HandshakeTimeout = TimeSpan.FromSeconds(30) });
        var registry = new RpcRegistry();
        registry.Add(new EchoHandler());
        var server = new RpcServer(transport, registry);
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.ClientConnected += _ => connected.TrySetResult();
        server.Start();

        // A silent client holds its handshake open.
        using var silent = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await silent.ConnectAsync(IPAddress.Loopback, transport.Port);

        var connection = new TcpClientConnection(
            new RpcOptions(), new TcpOptions { Host = "127.0.0.1", Port = transport.Port });
        var client = new RpcClient(connection, new RpcRegistry());
        await connection.ConnectAsync();
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(10));

        RpcResult<Handshake> result = await client
            .InvokeAsync<Handshake, Handshake>(300, new Handshake(41))
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(result.IsSuccess, "code=" + result.ErrorCode + " message=" + result.ErrorMessage);
        Assert.Equal(42, result.Value.ProtocolVersion);
        server.Close();
    }

    [Fact]
    public async Task SilentClientIsDroppedAtTheHandshakeDeadline()
    {
        var transport = new TcpServerTransport(
            new RpcOptions(),
            new TcpOptions { HandshakeTimeout = TimeSpan.FromSeconds(1) });
        Task<IServerConnection?> accept = transport.AcceptAsync();
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(IPAddress.Loopback, transport.Port);

        // Read the server handshake, then send nothing.
        await ReadFrameAsync(socket);

        // The transport waits for the handshake frame, then closes the socket.
        byte[] buffer = new byte[1];
        int read = await socket
            .ReceiveAsync(buffer, SocketFlags.None)
            .WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(0, read);
        Assert.False(accept.IsCompleted);
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
        var disconnected = new TaskCompletionSource<RpcDisconnectReason>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.Disconnected += reason => disconnected.TrySetResult(reason);
        await client.ConnectAsync();
        await accept;

        transport.Close();
        RpcDisconnectReason reason = await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(RpcDisconnectReason.PeerClosed, reason);
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
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.ClientConnected += _ => connected.TrySetResult();
        server.Start();
        var connection = new TcpClientConnection(
            new RpcOptions(), new TcpOptions { Host = "127.0.0.1", Port = transport.Port });
        var client = new RpcClient(connection, new RpcRegistry());
        await connection.ConnectAsync();
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(10));

        RpcResult<Handshake> result = await client.InvokeAsync<Handshake, Handshake>(300, new Handshake(41));

        Assert.True(result.IsSuccess, "code=" + result.ErrorCode + " message=" + result.ErrorMessage);
        Assert.Equal(42, result.Value.ProtocolVersion);
        server.Close();
    }

    [Fact]
    public async Task VersionMismatchClosesTheServerConnection()
    {
        var transport = new TcpServerTransport(new RpcOptions(), new TcpOptions());
        var server = new RpcServer(transport, new RpcRegistry());
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.ClientConnected += _ => connected.TrySetResult();
        server.Start();

        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(IPAddress.Loopback, transport.Port);
        await ReadFrameAsync(socket);
        await SendFrameAsync(socket, new Handshake(1).Serialize());
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // A frame with version 2 ends the connection.
        var envelope = new Envelope(2, (byte)RpcMessageKind.Notification, 0UL, 1UL, null, null);
        await SendFrameAsync(socket, envelope.Serialize());

        byte[] buffer = new byte[1];
        int read = await socket
            .ReceiveAsync(buffer, SocketFlags.None)
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(0, read);
        server.Close();
    }

    [Fact]
    public async Task MaximumPayloadRoundTripsAtTheDefaultCap()
    {
        var registry = new RpcRegistry();
        registry.Add<Blob, Blob>(new BlobEchoHandler(), 400);
        var transport = new TcpServerTransport(new RpcOptions(), new TcpOptions());
        var server = new RpcServer(transport, registry);
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.ClientConnected += _ => connected.TrySetResult();
        server.Start();
        var connection = new TcpClientConnection(
            new RpcOptions(), new TcpOptions { Host = "127.0.0.1", Port = transport.Port });
        var client = new RpcClient(connection, new RpcRegistry());
        await connection.ConnectAsync();
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // The blob codec adds five bytes, so a 65,530-byte array makes a
        // 65,535-byte payload. The frame is 65,570 bytes, the default cap.
        var request = new Blob(new byte[65530]);
        RpcResult<Blob> result = await client
            .InvokeAsync<Blob, Blob>(400, in request)
            .WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(65535, request.GetSize());
        Assert.True(result.IsSuccess, "code=" + result.ErrorCode + " message=" + result.ErrorMessage);
        Assert.Equal(65530, result.Value.Data!.Length);
        server.Close();
    }

    [Fact]
    public async Task PayloadAboveTheCapIsRejected()
    {
        var registry = new RpcRegistry();
        registry.Add<Blob, Blob>(new BlobEchoHandler(), 400);
        var transport = new TcpServerTransport(new RpcOptions(), new TcpOptions());
        var server = new RpcServer(transport, registry);
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.ClientConnected += _ => connected.TrySetResult();
        server.Start();
        var connection = new TcpClientConnection(
            new RpcOptions(), new TcpOptions { Host = "127.0.0.1", Port = transport.Port });
        var client = new RpcClient(connection, new RpcRegistry());
        await connection.ConnectAsync();
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // One more byte makes the frame 65,571 bytes, one above the default cap.
        var request = new Blob(new byte[65531]);
        RpcResult<Blob> result = await client.InvokeAsync<Blob, Blob>(400, in request);

        Assert.False(result.IsSuccess);
        Assert.Equal((int)RpcErrorCode.InvalidRequest, result.ErrorCode);
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
        serverSide.Disconnected += _ => removed.TrySetResult();
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
            serverSide.Disconnected += _ => removed.TrySetResult();
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

    [Fact]
    public async Task RejectedSendDoesNotAllocate()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Task<Socket> accept = listener.AcceptSocketAsync();
            using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await client.ConnectAsync(IPAddress.Loopback, port);
            using Socket server = await accept;

            var connection = new TcpConnection(
                client, new RpcOptions(), new TcpOptions { SendQueueBytes = 32 });
            byte[] frame = new byte[64];

            // Warm up the rejected path.
            Assert.False(connection.TrySend(frame));

            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 1000; index++)
            {
                Assert.False(connection.TrySend(frame));
            }

            long allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
            Assert.Equal(allocatedBefore, allocatedAfter);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task PartialSendDeliversTheWholeFrame()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Task<Socket> accept = listener.AcceptSocketAsync();
            using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            // A small send buffer forces the first send call to be partial.
            client.SendBufferSize = 4096;
            await client.ConnectAsync(IPAddress.Loopback, port);
            using Socket server = await accept;

            var connection = new TcpConnection(
                client, new RpcOptions(), new TcpOptions { SendQueueBytes = 1 << 20 });
            byte[] frame = new byte[65536];
            for (int index = 0; index < frame.Length; index++)
            {
                frame[index] = (byte)index;
            }

            Assert.True(connection.TrySend(frame));

            byte[] received = new byte[frame.Length];
            await ReadExactAsync(server, received).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(frame, received);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task SendFailureDuringAPartlySentFrameFiresDisconnected()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Task<Socket> accept = listener.AcceptSocketAsync();
            using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            client.SendBufferSize = 4096;
            await client.ConnectAsync(IPAddress.Loopback, port);
            Socket server = await accept;
            // Close the peer before the send so the frame cannot fit a buffer.
            server.Dispose();

            var connection = new TcpConnection(
                client, new RpcOptions(), new TcpOptions { SendQueueBytes = 1 << 20 });
            var disconnected = new TaskCompletionSource<RpcDisconnectReason>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            connection.Disconnected += reason => disconnected.TrySetResult(reason);
            Assert.True(connection.TrySend(new byte[256 * 1024]));

            RpcDisconnectReason actual = await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(RpcDisconnectReason.WriteError, actual);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task SilentPeerClosesWithIdleTimeout()
    {
        var transport = new TcpServerTransport(
            new RpcOptions(), new TcpOptions { IdleTimeout = TimeSpan.FromMilliseconds(250) });
        Task<IServerConnection?> accept = transport.AcceptAsync();
        using Socket socket = await ConnectRawAsync(transport);
        IServerConnection serverSide = (await accept)!;
        var disconnected = new TaskCompletionSource<RpcDisconnectReason>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        serverSide.Disconnected += reason => disconnected.TrySetResult(reason);

        RpcDisconnectReason actual = await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(RpcDisconnectReason.IdleTimeout, actual);
        Assert.False(serverSide.IsConnected);
        transport.Close();
    }

    [Fact]
    public async Task IdleDeadlineResetsOnARead()
    {
        (Socket client, Socket server) = await ConnectPairAsync();
        using (client)
        using (server)
        {
            var clock = new FakeClock();
            var options = new RpcOptions { TimestampNanos = clock.Now };
            var tcpOptions = new TcpOptions { IdleTimeout = TimeSpan.FromSeconds(30) };
            var connection = new TcpConnection(client, options, tcpOptions);

            await server.SendAsync(Frame([1, 2, 3]), SocketFlags.None);
            Assert.True(await connection.ReceiveOnceAsync().WaitAsync(TimeSpan.FromSeconds(10)));

            // The read landed at clock zero, so the deadline is 30 seconds out.
            clock.Advance(TimeSpan.FromSeconds(29));
            Assert.False(connection.IsIdleExpired());

            clock.Advance(TimeSpan.FromSeconds(2));
            Assert.True(connection.IsIdleExpired());
            connection.Close();
        }
    }

    [Fact]
    public async Task LocalCloseReportsLocalClose()
    {
        (Socket client, Socket server) = await ConnectPairAsync();
        using (client)
        using (server)
        {
            var connection = new TcpConnection(client, new RpcOptions(), new TcpOptions());
            var disconnected = new TaskCompletionSource<RpcDisconnectReason>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            connection.Disconnected += reason => disconnected.TrySetResult(reason);

            connection.Close();

            Assert.Equal(
                RpcDisconnectReason.LocalClose,
                await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        }
    }

    [Fact]
    public async Task PeerEofReportsPeerClosed()
    {
        (Socket client, Socket server) = await ConnectPairAsync();
        using (client)
        using (server)
        {
            var connection = new TcpConnection(client, new RpcOptions(), new TcpOptions());
            var disconnected = new TaskCompletionSource<RpcDisconnectReason>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            connection.Disconnected += reason => disconnected.TrySetResult(reason);
            connection.StartReceiving();

            server.Dispose();

            Assert.Equal(
                RpcDisconnectReason.PeerClosed,
                await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        }
    }

    [Fact]
    public async Task ProtocolViolationReportsProtocolViolation()
    {
        (Socket client, Socket server) = await ConnectPairAsync();
        using (client)
        using (server)
        {
            var connection = new TcpConnection(client, new RpcOptions(), new TcpOptions());
            var disconnected = new TaskCompletionSource<RpcDisconnectReason>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            connection.Disconnected += reason => disconnected.TrySetResult(reason);
            connection.StartReceiving();

            // A negative length prefix fails the decoder.
            await server.SendAsync(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, SocketFlags.None);

            Assert.Equal(
                RpcDisconnectReason.ProtocolViolation,
                await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        }
    }

    [Fact]
    public async Task WriteFailureReportsWriteError()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Task<Socket> accept = listener.AcceptSocketAsync();
            using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await client.ConnectAsync(IPAddress.Loopback, port);
            using Socket server = await accept;

            var connection = new TcpConnection(client, new RpcOptions(), new TcpOptions());
            var disconnected = new TaskCompletionSource<RpcDisconnectReason>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            connection.Disconnected += reason => disconnected.TrySetResult(reason);

            // A disposed socket fails the drain before it writes a byte.
            client.Dispose();
            Assert.True(connection.TrySend(new byte[64]));

            Assert.Equal(
                RpcDisconnectReason.WriteError,
                await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task HandshakeFailureReportsHandshakeFailed()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var connection = new TcpClientConnection(
                new RpcOptions(),
                new TcpOptions
                {
                    Host = "127.0.0.1",
                    Port = port,
                    HandshakeTimeout = TimeSpan.FromMilliseconds(100),
                });
            var disconnected = new TaskCompletionSource<RpcDisconnectReason>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            connection.Disconnected += reason => disconnected.TrySetResult(reason);

            // The listener accepts but never answers the handshake.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connection.ConnectAsync());

            Assert.Equal(
                RpcDisconnectReason.HandshakeFailed,
                await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task SustainedBacklogClosesWithBacklogLimit()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Task<Socket> accept = listener.AcceptSocketAsync();
            using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            client.SendBufferSize = 1024;
            await client.ConnectAsync(IPAddress.Loopback, port);
            using Socket server = await accept;

            var clock = new FakeClock();
            var options = new RpcOptions { TimestampNanos = clock.Now };
            var tcpOptions = new TcpOptions
            {
                SendQueueBytes = 8 << 20,
                SendBacklogDisconnectBytes = 128 * 1024,
                SendBacklogDisconnectMs = 1000,
            };
            var connection = new TcpConnection(client, options, tcpOptions);
            var disconnected = new TaskCompletionSource<RpcDisconnectReason>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            connection.Disconnected += reason => disconnected.TrySetResult(reason);

            // The large frame blocks the drain while the peer does not read.
            // The small frames stay queued and hold the backlog above threshold.
            Assert.True(connection.TrySend(new byte[4 << 20]));
            byte[] frame = new byte[4096];
            int threshold = tcpOptions.SendBacklogDisconnectBytes;
            for (int index = 0; index < 64 && connection.QueuedBytes < threshold; index++)
            {
                clock.Advance(TimeSpan.FromMilliseconds(10));
                Assert.True(connection.TrySend(frame));
            }

            clock.Advance(TimeSpan.FromMilliseconds(1000));
            Assert.True(connection.TrySend(frame));

            RpcDisconnectReason reason = await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(RpcDisconnectReason.BacklogLimit, reason);
            Assert.False(connection.IsConnected);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task AcceptedFramesArriveInOrderUnderTheBacklogPolicy()
    {
        (Socket client, Socket server) = await ConnectPairAsync();
        using (client)
        using (server)
        {
            var tcpOptions = new TcpOptions
            {
                SendQueueBytes = 1 << 20,
                SendBacklogDisconnectBytes = 256,
                SendBacklogDisconnectMs = 60000,
            };
            var connection = new TcpConnection(client, new RpcOptions(), tcpOptions);

            for (int index = 0; index < 64; index++)
            {
                Assert.True(connection.TrySend(Frame(BitConverter.GetBytes(index))));
            }

            for (int index = 0; index < 64; index++)
            {
                byte[] payload = await ReadFrameAsync(server).WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(index, BinaryPrimitives.ReadInt32LittleEndian(payload));
            }

            connection.Close();
        }
    }

    [Fact]
    public async Task ConnectTimeoutBoundsABlackholedRoute()
    {
        var connection = new TcpClientConnection(
            new RpcOptions(),
            new TcpOptions
            {
                Host = "192.0.2.1",
                Port = 9,
                ConnectTimeout = TimeSpan.FromMilliseconds(200),
            });
        var elapsed = Stopwatch.StartNew();

        await Assert.ThrowsAsync<TimeoutException>(() => connection.ConnectAsync());

        elapsed.Stop();
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(5));
        Assert.False(connection.IsConnected);
    }

    [Fact]
    public async Task LoopbackBindAcceptsALoopbackClientOnTheReportedPort()
    {
        var transport = new TcpServerTransport(
            new RpcOptions(), new TcpOptions { BindAddress = IPAddress.Loopback });
        int port = transport.Port;
        Assert.True(port > 0);

        Task<IServerConnection?> accept = transport.AcceptAsync();
        var client = new TcpClientConnection(
            new RpcOptions(), new TcpOptions { Host = "127.0.0.1", Port = port });
        await client.ConnectAsync();
        IServerConnection serverSide = (await accept)!;

        Assert.True(serverSide.IsConnected);
        Assert.True(client.IsConnected);
        client.Close();
        transport.Close();
    }

    [Fact]
    public async Task ReceiveOnceRentsAndReturnsTheBufferOnANormalRead()
    {
        (Socket client, Socket server) = await ConnectPairAsync();
        using (client)
        using (server)
        {
            var pool = new CountingPool();
            var connection = new TcpConnection(client, new RpcOptions(), new TcpOptions(), pool);
            List<byte[]> frames = [];
            connection.FrameReceived += frame => frames.Add(frame.ToArray());

            await server.SendAsync(Frame([1, 2, 3]), SocketFlags.None);
            bool more = await connection.ReceiveOnceAsync().WaitAsync(TimeSpan.FromSeconds(10));

            Assert.True(more);
            Assert.Single(frames);
            Assert.Equal(1, pool.Rents);
            Assert.Equal(1, pool.Returns);
            Assert.Equal(0, pool.Outstanding);
        }
    }

    [Fact]
    public async Task ReceiveOnceRentsTheBufferForEachRead()
    {
        (Socket client, Socket server) = await ConnectPairAsync();
        using (client)
        using (server)
        {
            var pool = new CountingPool();
            var connection = new TcpConnection(client, new RpcOptions(), new TcpOptions(), pool);

            await server.SendAsync(Frame([1]), SocketFlags.None);
            Assert.True(await connection.ReceiveOnceAsync().WaitAsync(TimeSpan.FromSeconds(10)));

            await server.SendAsync(Frame([2]), SocketFlags.None);
            Assert.True(await connection.ReceiveOnceAsync().WaitAsync(TimeSpan.FromSeconds(10)));

            Assert.Equal(2, pool.Rents);
            Assert.Equal(2, pool.Returns);
            Assert.Equal(0, pool.Outstanding);
        }
    }

    [Fact]
    public async Task ReceiveOnceReturnsTheBufferOnADecoderRejection()
    {
        (Socket client, Socket server) = await ConnectPairAsync();
        using (client)
        using (server)
        {
            var pool = new CountingPool();
            var connection = new TcpConnection(client, new RpcOptions(), new TcpOptions(), pool);

            // A zero-length frame fails the decoder.
            await server.SendAsync(new byte[4], SocketFlags.None);
            bool more = await connection.ReceiveOnceAsync().WaitAsync(TimeSpan.FromSeconds(10));

            Assert.False(more);
            Assert.Equal(1, pool.Rents);
            Assert.Equal(1, pool.Returns);
            Assert.Equal(0, pool.Outstanding);
        }
    }

    [Fact]
    public async Task ReceiveOnceReturnsTheBufferOnASocketFailure()
    {
        (Socket client, Socket server) = await ConnectPairAsync();
        using (client)
        using (server)
        {
            var pool = new CountingPool();
            var connection = new TcpConnection(client, new RpcOptions(), new TcpOptions(), pool);
            // Disposing the socket forces the next receive to throw.
            connection.Close();

            bool more = await connection.ReceiveOnceAsync().WaitAsync(TimeSpan.FromSeconds(10));

            Assert.False(more);
            Assert.Equal(1, pool.Rents);
            Assert.Equal(1, pool.Returns);
            Assert.Equal(0, pool.Outstanding);
        }
    }

    [Fact]
    public async Task IdleConnectionHoldsNoReceiveBuffer()
    {
        (Socket client, Socket server) = await ConnectPairAsync();
        using (client)
        using (server)
        {
            var pool = new CountingPool();
            var connection = new TcpConnection(client, new RpcOptions(), new TcpOptions(), pool);
            connection.StartReceiving();

            await server.SendAsync(Frame([1, 2, 3]), SocketFlags.None);
            await pool.FirstReturn.WaitAsync(TimeSpan.FromSeconds(10));

            // The read returned its buffer. The loop now waits for data
            // without a buffer, so the idle connection holds none.
            Assert.Equal(0, pool.Outstanding);
        }
    }

    [Fact]
    public async Task ConnectionShrinksAnIdleDecoder()
    {
        (Socket client, Socket server) = await ConnectPairAsync();
        using (client)
        using (server)
        {
            var clock = new FakeClock();
            var options = new RpcOptions { TimestampNanos = clock.Now };
            var tcpOptions = new TcpOptions { DecoderIdleShrinkAfter = TimeSpan.FromSeconds(30) };
            var connection = new TcpConnection(client, options, tcpOptions);

            await server.SendAsync(Frame(new byte[5000]), SocketFlags.None);
            Assert.True(await connection.ReceiveOnceAsync().WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.True(connection.DecoderBufferBytes > TcpFrameDecoder.StartBufferBytes);

            clock.Advance(TimeSpan.FromSeconds(30));

            Assert.True(connection.TryShrinkDecoder());
            Assert.Equal(TcpFrameDecoder.StartBufferBytes, connection.DecoderBufferBytes);
            connection.Close();
        }
    }

    [Fact]
    public async Task ConnectionKeepsADecoderThatRecentlyRead()
    {
        (Socket client, Socket server) = await ConnectPairAsync();
        using (client)
        using (server)
        {
            var clock = new FakeClock();
            var options = new RpcOptions { TimestampNanos = clock.Now };
            var tcpOptions = new TcpOptions { DecoderIdleShrinkAfter = TimeSpan.FromSeconds(30) };
            var connection = new TcpConnection(client, options, tcpOptions);

            await server.SendAsync(Frame(new byte[5000]), SocketFlags.None);
            Assert.True(await connection.ReceiveOnceAsync().WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.True(connection.DecoderBufferBytes > TcpFrameDecoder.StartBufferBytes);

            // Below the idle period the decoder stays grown.
            clock.Advance(TimeSpan.FromSeconds(29));

            Assert.False(connection.TryShrinkDecoder());
            Assert.True(connection.DecoderBufferBytes > TcpFrameDecoder.StartBufferBytes);
            connection.Close();
        }
    }

    [Fact]
    public async Task LargeFrameStreamDoesNotShrinkTheDecoder()
    {
        (Socket client, Socket server) = await ConnectPairAsync();
        using (client)
        using (server)
        {
            var clock = new FakeClock();
            var options = new RpcOptions { TimestampNanos = clock.Now };
            var tcpOptions = new TcpOptions { DecoderIdleShrinkAfter = TimeSpan.FromSeconds(30) };
            var connection = new TcpConnection(client, options, tcpOptions);

            for (int index = 0; index < 3; index++)
            {
                await server.SendAsync(Frame(new byte[5000]), SocketFlags.None);
                Assert.True(await connection.ReceiveOnceAsync().WaitAsync(TimeSpan.FromSeconds(10)));

                // Each read resets the idle period, so the sweep never shrinks
                // the decoder between the frames.
                clock.Advance(TimeSpan.FromSeconds(1));
                Assert.False(connection.TryShrinkDecoder());
            }

            Assert.True(connection.DecoderBufferBytes > TcpFrameDecoder.StartBufferBytes);
            connection.Close();
        }
    }

    // A settable monotonic timestamp source. Tests advance it to cross the
    // decoder idle period.
    private sealed class FakeClock
    {
        private long _nanos;

        public long Now()
        {
            return _nanos;
        }

        public void Advance(TimeSpan span)
        {
            _nanos += span.Ticks * 100L;
        }
    }

    // A pooled array source that counts the rent and the return. It proves the
    // receive loop rents per read and returns the buffer on every path.
    private sealed class CountingPool : ArrayPool<byte>
    {
        private readonly ArrayPool<byte> _inner = ArrayPool<byte>.Shared;
        private readonly TaskCompletionSource _firstReturn =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _rents;
        private int _returns;

        public int Rents => Volatile.Read(ref _rents);

        public int Returns => Volatile.Read(ref _returns);

        public int Outstanding => Rents - Returns;

        public Task FirstReturn => _firstReturn.Task;

        public override byte[] Rent(int minimumLength)
        {
            Interlocked.Increment(ref _rents);
            return _inner.Rent(minimumLength);
        }

        public override void Return(byte[] array, bool clearArray = false)
        {
            Interlocked.Increment(ref _returns);
            _inner.Return(array, clearArray);
            _firstReturn.TrySetResult();
        }
    }

    private static byte[] Frame(params byte[] payload)
    {
        byte[] frame = new byte[4 + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, payload.Length);
        payload.CopyTo(frame, 4);
        return frame;
    }

    private static async Task<(Socket Client, Socket Server)> ConnectPairAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Task<Socket> accept = listener.AcceptSocketAsync();
            var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await client.ConnectAsync(IPAddress.Loopback, port);
            Socket server = await accept;
            return (client, server);
        }
        finally
        {
            listener.Stop();
        }
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
