using BenchmarkDotNet.Attributes;
using NsdRpc.Benchmarks.Wire;
using NsdRpc.Tcp;
using NsdRpc.Wire;

namespace NsdRpc.Benchmarks;

// One request and one response over a real loopback socket pair. The
// handshake case is a small frame. The blob case is a 4-kilobyte frame.
[MemoryDiagnoser]
public class TcpRoundTripBenchmarks
{
    private const int BlobPayloadBytes = 4096;

    private TcpServerTransport _transport = null!;
    private RpcServer _server = null!;
    private RpcClient _client = null!;
    private Handshake _handshake;
    private Blob _blob;

    [GlobalSetup]
    public async Task Setup()
    {
        var rpcOptions = new RpcOptions();
        var registry = new RpcRegistry();
        registry.Add<Handshake, Handshake>(
            new EchoHandshakeHandler(), BenchmarkMethods.EchoHandshake);
        registry.Add<Blob, Blob>(
            new EchoBlobHandler(), BenchmarkMethods.EchoBlob);

        _transport = new TcpServerTransport(
            rpcOptions, new TcpOptions { Host = "127.0.0.1", Port = 0 });
        _server = new RpcServer(_transport, registry, rpcOptions);
        _server.Start();

        var clientConnection = new TcpClientConnection(
            rpcOptions,
            new TcpOptions { Host = "127.0.0.1", Port = _transport.Port });
        await clientConnection.ConnectAsync().ConfigureAwait(false);
        _client = new RpcClient(clientConnection, new RpcRegistry(), rpcOptions);

        _handshake = new Handshake(7);
        _blob = new Blob(new byte[BlobPayloadBytes]);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _server.Close();
    }

    [Benchmark]
    public async Task<RpcResult<Handshake>> RoundTripHandshake()
    {
        return await _client
            .InvokeAsync<Handshake, Handshake>(BenchmarkMethods.EchoHandshake, in _handshake)
            .ConfigureAwait(false);
    }

    [Benchmark]
    public async Task<RpcResult<Blob>> RoundTripBlob()
    {
        return await _client
            .InvokeAsync<Blob, Blob>(BenchmarkMethods.EchoBlob, in _blob)
            .ConfigureAwait(false);
    }
}
