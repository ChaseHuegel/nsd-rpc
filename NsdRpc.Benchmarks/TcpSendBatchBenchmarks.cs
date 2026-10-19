using System.Buffers.Binary;
using BenchmarkDotNet.Attributes;
using NsdRpc.Tcp;

namespace NsdRpc.Benchmarks;

// Sends the same small frames with and without the send batch interval. Each
// side receives the whole stream, so the difference is the socket write count
// and the drain loop cost.
[MemoryDiagnoser]
public class TcpSendBatchBenchmarks
{
    private const int FrameCount = 4096;

    private const int PayloadBytes = 4;

    private static readonly byte[] FrameBytes = CreateFrame();

    private Receiver _immediateReceiver = null!;
    private Receiver _batchedReceiver = null!;
    private TcpClientConnection _immediate = null!;
    private TcpClientConnection _batched = null!;
    private TcpServerTransport _immediateTransport = null!;
    private TcpServerTransport _batchedTransport = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        var rpcOptions = new RpcOptions();
        (_immediate, _immediateTransport, _immediateReceiver) = await CreatePairAsync(
            rpcOptions, TimeSpan.Zero).ConfigureAwait(false);
        (_batched, _batchedTransport, _batchedReceiver) = await CreatePairAsync(
            rpcOptions, TimeSpan.FromMilliseconds(1)).ConfigureAwait(false);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _immediate.Close();
        _batched.Close();
        _immediateTransport.Close();
        _batchedTransport.Close();
    }

    [Benchmark(Baseline = true)]
    public Task SendImmediately()
    {
        return SendAsync(_immediate, _immediateReceiver);
    }

    [Benchmark]
    public Task SendBatched()
    {
        return SendAsync(_batched, _batchedReceiver);
    }

    private static async Task SendAsync(TcpClientConnection connection, Receiver receiver)
    {
        Interlocked.Exchange(ref receiver.Frames, 0);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.Done = done;
        for (int index = 0; index < FrameCount; index++)
        {
            if (!connection.TrySend(FrameBytes))
            {
                throw new InvalidOperationException("The send was rejected.");
            }
        }

        await done.Task.ConfigureAwait(false);
    }

    private static async Task<(TcpClientConnection Client, TcpServerTransport Transport, Receiver Receiver)>
        CreatePairAsync(RpcOptions rpcOptions, TimeSpan batchInterval)
    {
        var transport = new TcpServerTransport(
            rpcOptions,
            new TcpOptions
            {
                SendBatchInterval = batchInterval,
                SendBatchMaxBytes = 65536,
            });
        Task<IServerConnection?> accept = transport.AcceptAsync();
        var client = new TcpClientConnection(
            rpcOptions,
            new TcpOptions
            {
                Host = "127.0.0.1",
                Port = transport.Port,
                SendBatchInterval = batchInterval,
                SendBatchMaxBytes = 65536,
            });
        await client.ConnectAsync().ConfigureAwait(false);
        IServerConnection server = (await accept.ConfigureAwait(false))!;
        var receiver = new Receiver();
        server.FrameReceived += receiver.OnFrame;
        return (client, transport, receiver);
    }

    private static byte[] CreateFrame()
    {
        byte[] frame = new byte[4 + PayloadBytes];
        BinaryPrimitives.WriteInt32LittleEndian(frame, PayloadBytes);
        return frame;
    }

    private sealed class Receiver
    {
        public TaskCompletionSource? Done;

        public int Frames;

        public void OnFrame(ReadOnlySpan<byte> frame)
        {
            if (Interlocked.Increment(ref Frames) == FrameCount)
            {
                Done?.TrySetResult();
            }
        }
    }
}
