using System.Net;
using System.Net.Sockets;
using NsdRpc;
using NsdRpc.Tcp;
using NsdRpc.Wire;

namespace NsdRpc.Tests;

public sealed class ConnectionStatsTests
{
    [RpcMethod(301)]
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
    public async Task StatsPinExactCountsForAKnownExchange()
    {
        var registry = new RpcRegistry();
        registry.Add(new EchoHandler());
        var transport = new TcpServerTransport(new RpcOptions(), new TcpOptions());
        Task<IServerConnection?> accept = transport.AcceptAsync();
        var clientConnection = new TcpClientConnection(
            new RpcOptions(), new TcpOptions { Host = "127.0.0.1", Port = transport.Port });
        await clientConnection.ConnectAsync();
        IServerConnection serverConnection = (await accept)!;

        var client = new RpcPeer(clientConnection);
        var server = new RpcPeer(serverConnection);
        server.DispatchHook = new RpcDispatcher(registry, server).DispatchAsync;

        RpcResult<Handshake> result = await client
            .InvokeAsync<Handshake, Handshake>(301, new Handshake(41))
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(result.IsSuccess);
        RpcConnectionStats clientStats = client.Stats;
        RpcConnectionStats serverStats = server.Stats;
        Assert.Equal(1, clientStats.PacketsSent);
        Assert.Equal(1, clientStats.PacketsReceived);
        Assert.Equal(1, clientStats.Writes);
        Assert.Equal(1, serverStats.PacketsSent);
        Assert.Equal(1, serverStats.PacketsReceived);
        Assert.Equal(1, serverStats.Writes);
        Assert.Equal(clientStats.BytesSent, serverStats.BytesReceived);
        Assert.Equal(clientStats.BytesReceived, serverStats.BytesSent);
        Assert.Equal(0, clientStats.QueuedBytes);
        Assert.Equal(0, clientStats.RejectedFrames);
        transport.Close();
    }

    [Fact]
    public async Task QueuedBytesAndPeakTrackAStalledRead()
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

            var connection = new TcpConnection(
                client, new RpcOptions(), new TcpOptions { SendQueueBytes = 8 << 20 });

            // The large frame blocks the drain while the peer does not read.
            Assert.True(connection.TrySend(new byte[4 << 20]));
            for (int index = 0; index < 4; index++)
            {
                Assert.True(connection.TrySend(new byte[4096]));
            }

            RpcConnectionStats stats = connection.Stats;
            Assert.True(stats.QueuedBytes > 0);
            Assert.True(stats.PeakQueuedBytes >= stats.QueuedBytes);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task RejectedFramesAreCountedWithoutLoss()
    {
        (Socket client, Socket server) = await ConnectPairAsync();
        using (client)
        using (server)
        {
            var connection = new TcpConnection(client, new RpcOptions(), new TcpOptions());
            connection.Close();

            const int Workers = 8;
            const int SendsPerWorker = 1000;
            Task[] workers = new Task[Workers];
            for (int index = 0; index < Workers; index++)
            {
                workers[index] = Task.Run(() =>
                {
                    for (int send = 0; send < SendsPerWorker; send++)
                    {
                        Assert.False(connection.TrySend(new byte[4]));
                    }
                });
            }

            await Task.WhenAll(workers);

            Assert.Equal(Workers * SendsPerWorker, connection.Stats.RejectedFrames);
        }
    }

    [Fact]
    public void PeerStatsReturnDefaultsWithoutTheCapability()
    {
        var peer = new RpcPeer(new InMemoryConnection());

        RpcConnectionStats stats = peer.Stats;

        Assert.Equal(default(RpcConnectionStats), stats);
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
}
