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
internal sealed class TcpConnection : IRpcConnection, IOwnedFrameSink
{
    private readonly Socket _socket;
    private readonly TcpFrameDecoder _decoder;
    private readonly TcpOptions _tcpOptions;
    private readonly ArrayPool<byte> _receiveBufferPool;
    private readonly RpcFrameHandler _onFrame;
    private readonly object _sendLock = new();
    private readonly Queue<SendBuffer> _sendQueue = new();
    private readonly CancellationTokenSource _lifetime = new();
    private Action? _disconnectedHandlers;
    private RpcFrameHandler? _frameReceivedHandlers;
    private int _queuedBytes;
    private bool _sending;
    private bool _receiving;
    private int _disconnected;

    public TcpConnection(Socket socket, RpcOptions rpcOptions, TcpOptions tcpOptions)
        : this(socket, rpcOptions, tcpOptions, ArrayPool<byte>.Shared)
    {
    }

    // A test seam. The pool lets a test observe the per-read rent and the
    // return on each receive path.
    internal TcpConnection(
        Socket socket, RpcOptions rpcOptions, TcpOptions tcpOptions, ArrayPool<byte> receiveBufferPool)
    {
        _socket = socket;
        _tcpOptions = tcpOptions;
        _receiveBufferPool = receiveBufferPool;
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
        // Check before the copy, so a rejected send allocates nothing.
        if (_disconnected != 0 || _queuedBytes + frame.Length > _tcpOptions.SendQueueBytes)
        {
            return false;
        }

        byte[] copy = frame.ToArray();
        return Enqueue(copy, copy.Length, pooled: false);
    }

    public bool TrySendOwned(byte[] frame, int length)
    {
        if (Enqueue(frame, length, pooled: true))
        {
            return true;
        }

        ArrayPool<byte>.Shared.Return(frame, clearArray: true);
        return false;
    }

    private bool Enqueue(byte[] frame, int length, bool pooled)
    {
        lock (_sendLock)
        {
            if (_disconnected != 0)
            {
                return false;
            }

            if (_queuedBytes + length > _tcpOptions.SendQueueBytes)
            {
                return false;
            }

            _sendQueue.Enqueue(new SendBuffer(frame, length, pooled));
            _queuedBytes += length;
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
            SendBuffer buffer;
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
                ReturnBuffer(buffer);
                ReturnQueuedBuffers();
                FireDisconnected();
                return;
            }

            ReturnBuffer(buffer);
        }
    }

    // Returns a buffer to the pool. The copy path owns a plain array, so it
    // is dropped.
    private static void ReturnBuffer(SendBuffer buffer)
    {
        if (buffer.Pooled)
        {
            ArrayPool<byte>.Shared.Return(buffer.Frame, clearArray: true);
        }
    }

    private void ReturnQueuedBuffers()
    {
        while (true)
        {
            SendBuffer buffer;
            lock (_sendLock)
            {
                if (_sendQueue.Count == 0)
                {
                    return;
                }

                buffer = _sendQueue.Dequeue();
                _queuedBytes -= buffer.Length;
            }

            ReturnBuffer(buffer);
        }
    }

    private async Task ReceiveLoopAsync()
    {
        try
        {
            while (_disconnected == 0)
            {
                if (!await WaitForDataAsync().ConfigureAwait(false))
                {
                    break;
                }

                if (!await ReceiveOnceAsync().ConfigureAwait(false))
                {
                    break;
                }
            }
        }
        finally
        {
            FireDisconnected();
        }
    }

    // Waits until the socket has data. The wait holds no receive buffer, so an
    // idle connection holds none. A zero-length receive returns when the socket
    // is readable and does not consume the data. Returns false on a socket
    // failure or a close.
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "The receive loop treats every socket failure as a disconnect. It is a transport boundary.")]
    private async Task<bool> WaitForDataAsync()
    {
        try
        {
            await _socket
                .ReceiveAsync(Memory<byte>.Empty, SocketFlags.None, _lifetime.Token)
                .ConfigureAwait(false);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // Rents one receive buffer, reads once, and pushes the bytes. The buffer
    // returns before this method returns on every path: a normal read, a
    // decoder rejection, and a socket failure. Returns true when the loop must
    // continue. Tests drive one iteration directly to pin the rent and the
    // return.
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "The receive loop treats every socket failure as a disconnect. It is a transport boundary.")]
    internal async Task<bool> ReceiveOnceAsync()
    {
        byte[] buffer = _receiveBufferPool.Rent(_tcpOptions.ReceiveBufferBytes);
        try
        {
            int read;
            try
            {
                read = await _socket
                    .ReceiveAsync(buffer, SocketFlags.None, _lifetime.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                return false;
            }

            if (read == 0)
            {
                return false;
            }

            return _decoder.TryPush(buffer.AsSpan(0, read), _onFrame);
        }
        finally
        {
            _receiveBufferPool.Return(buffer, clearArray: true);
        }
    }

    private async Task SendAllAsync(byte[] data, CancellationToken cancellationToken)
    {
        await SendAllAsync(new SendBuffer(data, data.Length, pooled: false), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task SendAllAsync(SendBuffer buffer, CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int sent = await _socket.SendAsync(
                buffer.Frame.AsMemory(offset, buffer.Length - offset), SocketFlags.None, cancellationToken)
                .ConfigureAwait(false);
            if (sent <= 0)
            {
                throw new IOException("The socket sent no bytes.");
            }

            offset += sent;
        }
    }

    // One queued frame. A pooled frame comes from the shared pool and returns
    // to it after the send.
    private readonly struct SendBuffer
    {
        public SendBuffer(byte[] frame, int length, bool pooled)
        {
            Frame = frame;
            Length = length;
            Pooled = pooled;
        }

        public readonly byte[] Frame;

        public readonly int Length;

        public readonly bool Pooled;
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
