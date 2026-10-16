using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using NsdRpc;
using NsdRpc.Wire;

namespace NsdRpc.Tcp;

// The socket engine shared by the client and server connections. One receive
// loop pushes complete frames. A bounded send queue drains on demand. The
// handshake runs before StartReceiving, so the RPC engine only ever sees
// envelope frames.
internal sealed class TcpConnection : IRpcConnection
{
    private readonly Socket _socket;
    private readonly TcpFrameDecoder _decoder;
    private readonly TcpOptions _tcpOptions;
    private readonly RpcFrameHandler _onFrame;
    private readonly object _sendLock = new();
    private readonly Queue<byte[]> _sendQueue = new();
    private readonly CancellationTokenSource _lifetime = new();
    private Action? _disconnectedHandlers;
    private RpcFrameHandler? _frameReceivedHandlers;
    private int _queuedBytes;
    private bool _sending;
    private bool _receiving;
    private int _disconnected;

    public TcpConnection(Socket socket, RpcOptions rpcOptions, TcpOptions tcpOptions)
    {
        _socket = socket;
        _tcpOptions = tcpOptions;
        _decoder = new TcpFrameDecoder(rpcOptions.MaxFrameBytes);
        // One delegate for the connection. The receive loop must not allocate.
        _onFrame = OnFrame;
    }

    public bool IsConnected => _disconnected == 0;

    public event Action Disconnected
    {
        add => _disconnectedHandlers += value;
        remove => _disconnectedHandlers -= value;
    }

    public event RpcFrameHandler FrameReceived
    {
        add => _frameReceivedHandlers += value;
        remove => _frameReceivedHandlers -= value;
    }

    public bool TrySend(ReadOnlySpan<byte> frame)
    {
        if (_disconnected != 0)
        {
            return false;
        }

        byte[] copy = frame.ToArray();
        lock (_sendLock)
        {
            if (_disconnected != 0)
            {
                return false;
            }

            if (_queuedBytes + copy.Length > _tcpOptions.SendQueueBytes)
            {
                return false;
            }

            _sendQueue.Enqueue(copy);
            _queuedBytes += copy.Length;
            if (_sending)
            {
                return true;
            }

            _sending = true;
        }

        _ = DrainSendQueueAsync();
        return true;
    }

    public void StartReceiving()
    {
        if (_receiving)
        {
            return;
        }

        _receiving = true;
        _ = ReceiveLoopAsync();
    }

    public async Task RunHandshakeAsync()
    {
        using var handshake = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        if (_tcpOptions.HandshakeTimeout > TimeSpan.Zero)
        {
            handshake.CancelAfter(_tcpOptions.HandshakeTimeout);
        }

        CancellationToken token = handshake.Token;
        byte[] payload = new Handshake(TcpProtocol.Version).Serialize();
        byte[] frame = new byte[4 + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, payload.Length);
        payload.CopyTo(frame, 4);

        await SendAllAsync(frame, token).ConfigureAwait(false);
        byte[] theirs = await ReadOneFrameAsync(token).ConfigureAwait(false);
        Handshake other = Handshake.Deserialize((ReadOnlySpan<byte>)theirs);
        if (other.ProtocolVersion != TcpProtocol.Version)
        {
            throw new InvalidDataException("Handshake version mismatch.");
        }
    }

    public void Close()
    {
        FireDisconnected();
    }

    private async Task DrainSendQueueAsync()
    {
        while (true)
        {
            byte[]? buffer = null;
            lock (_sendLock)
            {
                if (_sendQueue.Count == 0)
                {
                    _sending = false;
                    return;
                }

                buffer = _sendQueue.Dequeue();
                _queuedBytes -= buffer.Length;
            }

            try
            {
                await SendAllAsync(buffer, _lifetime.Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                FireDisconnected();
                return;
            }
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "The receive loop treats every socket failure as a disconnect. It is a transport boundary.")]
    private async Task ReceiveLoopAsync()
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(_tcpOptions.ReceiveBufferBytes);
        try
        {
            while (_disconnected == 0)
            {
                int read;
                try
                {
                    read = await _socket.ReceiveAsync(buffer, SocketFlags.None, _lifetime.Token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    break;
                }

                if (read == 0)
                {
                    break;
                }

                if (!_decoder.TryPush(buffer.AsSpan(0, read), _onFrame))
                {
                    break;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            FireDisconnected();
        }
    }

    private async Task SendAllAsync(byte[] data, CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < data.Length)
        {
            int sent = await _socket.SendAsync(
                data.AsMemory(offset), SocketFlags.None, cancellationToken).ConfigureAwait(false);
            if (sent <= 0)
            {
                throw new IOException("The socket sent no bytes.");
            }

            offset += sent;
        }
    }

    private async Task<byte[]> ReadOneFrameAsync(CancellationToken cancellationToken)
    {
        byte[] header = new byte[4];
        await ReadExactAsync(header, 4, cancellationToken).ConfigureAwait(false);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > _decoder.MaxFrameBytes - 4)
        {
            throw new InvalidDataException("Bad handshake frame length.");
        }

        byte[] payload = new byte[length];
        await ReadExactAsync(payload, length, cancellationToken).ConfigureAwait(false);
        return payload;
    }

    private async Task ReadExactAsync(byte[] buffer, int count, CancellationToken cancellationToken)
    {
        int filled = 0;
        while (filled < count)
        {
            int read = await _socket.ReceiveAsync(
                buffer.AsMemory(filled, count - filled), SocketFlags.None, cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                throw new InvalidDataException("The connection closed during the handshake.");
            }

            filled += read;
        }
    }

    private void OnFrame(ReadOnlySpan<byte> frame)
    {
        _frameReceivedHandlers?.Invoke(frame);
    }

    private void FireDisconnected()
    {
        if (Interlocked.Exchange(ref _disconnected, 1) != 0)
        {
            return;
        }

        _lifetime.Cancel();
        try
        {
            _socket.Dispose();
        }
        catch (Exception)
        {
            // The socket may already be gone. Closing is best-effort.
        }

        _disconnectedHandlers?.Invoke();
        _disconnectedHandlers = null;
    }
}
