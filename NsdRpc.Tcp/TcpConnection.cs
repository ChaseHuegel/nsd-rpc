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
internal sealed class TcpConnection : IRpcConnection, IOwnedFrameSink, IRpcConnectionStats
{
    private readonly Socket _socket;
    private readonly TcpFrameDecoder _decoder;
    private readonly RpcOptions _rpcOptions;
    private readonly TcpOptions _tcpOptions;
    private readonly ArrayPool<byte> _receiveBufferPool;
    private readonly RpcFrameHandler _onFrame;
    private readonly object _sendLock = new();
    private readonly Queue<SendBuffer> _sendQueue = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly long _idleShrinkAfterNanos;
    private readonly TimeSpan _idleShrinkDelay;
    private readonly long _idleTimeoutNanos;
    private readonly SendBacklogTracker _backlog;
    private Action<RpcDisconnectReason>? _disconnectedHandlers;
    private RpcFrameHandler? _frameReceivedHandlers;
    private int _queuedBytes;
    private int _peakQueuedBytes;
    private long _packetsSent;
    private long _packetsReceived;
    private long _bytesSent;
    private long _bytesReceived;
    private long _writes;
    private long _rejectedFrames;
    private bool _sending;
    private bool _receiving;
    private int _disconnected;
    private int _idleSweepRunning;
    private long _lastReadNanos;
    private RpcDisconnectReason _receiveDisconnectReason = RpcDisconnectReason.ReadError;

    public TcpConnection(Socket socket, RpcOptions rpcOptions, TcpOptions tcpOptions)
        : this(socket, rpcOptions, tcpOptions, ArrayPool<byte>.Shared)
    {
    }

    // A test seam. The pool lets a test observe the per-read rent and the
    // return on each receive path.
    internal TcpConnection(
        Socket socket, RpcOptions rpcOptions, TcpOptions tcpOptions, ArrayPool<byte> receiveBufferPool)
    {
        TcpOptionValidation.Validate(tcpOptions);
        _socket = socket;
        _rpcOptions = rpcOptions;
        _tcpOptions = tcpOptions;
        _receiveBufferPool = receiveBufferPool;
        _decoder = new TcpFrameDecoder(rpcOptions.MaxFrameBytes);
        _lastReadNanos = rpcOptions.TimestampNanos();
        _idleShrinkAfterNanos = DurationNanos(tcpOptions.DecoderIdleShrinkAfter);
        _idleShrinkDelay = ClampDelay(tcpOptions.DecoderIdleShrinkAfter);
        _idleTimeoutNanos = DurationNanos(tcpOptions.IdleTimeout);
        _backlog = new SendBacklogTracker(
            tcpOptions.SendBacklogDisconnectBytes, tcpOptions.SendBacklogDisconnectMs);
        // One delegate for the connection. The receive loop must not allocate.
        _onFrame = OnFrame;
    }

    public bool IsConnected => _disconnected == 0;

    // The decoder buffer bytes. Tests read it to pin the idle shrink.
    internal int DecoderBufferBytes => _decoder.BufferBytes;

    // The queued send bytes. Tests read it to pin the backlog policy.
    internal int QueuedBytes => Volatile.Read(ref _queuedBytes);

    public RpcConnectionStats Stats => new(
        Interlocked.Read(ref _packetsSent),
        Interlocked.Read(ref _packetsReceived),
        Interlocked.Read(ref _bytesSent),
        Interlocked.Read(ref _bytesReceived),
        Volatile.Read(ref _queuedBytes),
        Volatile.Read(ref _peakQueuedBytes),
        Interlocked.Read(ref _writes),
        Interlocked.Read(ref _rejectedFrames));

    public event Action<RpcDisconnectReason> Disconnected
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
            Interlocked.Increment(ref _rejectedFrames);
            return false;
        }

        byte[] copy = frame.ToArray();
        if (Enqueue(copy, copy.Length, pooled: false))
        {
            return true;
        }

        Interlocked.Increment(ref _rejectedFrames);
        return false;
    }

    public bool TrySendOwned(byte[] frame, int length)
    {
        if (Enqueue(frame, length, pooled: true))
        {
            return true;
        }

        Interlocked.Increment(ref _rejectedFrames);
        ArrayPool<byte>.Shared.Return(frame, clearArray: true);
        return false;
    }

    private bool Enqueue(byte[] frame, int length, bool pooled)
    {
        bool closeForBacklog = false;
        bool drain = false;
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
            if (_queuedBytes > _peakQueuedBytes)
            {
                _peakQueuedBytes = _queuedBytes;
            }

            closeForBacklog = _backlog.ExceedsWindow(_queuedBytes, _rpcOptions.TimestampNanos());
            if (!closeForBacklog && !_sending)
            {
                _sending = true;
                drain = true;
            }
        }

        if (closeForBacklog)
        {
            ReturnQueuedBuffers();
            FireDisconnected(RpcDisconnectReason.BacklogLimit);
            return true;
        }

        if (drain)
        {
            _ = DrainSendQueueAsync();
        }

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

    public void Close(RpcDisconnectReason reason = RpcDisconnectReason.LocalClose)
    {
        FireDisconnected(reason);
    }

    private async Task DrainSendQueueAsync()
    {
        while (true)
        {
            SendBuffer buffer;
            bool closeForBacklog;
            lock (_sendLock)
            {
                if (_sendQueue.Count == 0)
                {
                    _sending = false;
                    return;
                }

                buffer = _sendQueue.Dequeue();
                _queuedBytes -= buffer.Length;
                closeForBacklog = _backlog.ExceedsWindow(
                    _queuedBytes, _rpcOptions.TimestampNanos());
            }

            if (closeForBacklog)
            {
                ReturnBuffer(buffer);
                ReturnQueuedBuffers();
                FireDisconnected(RpcDisconnectReason.BacklogLimit);
                return;
            }

            try
            {
                int writes = await SendAllAsync(buffer, _lifetime.Token).ConfigureAwait(false);
                Interlocked.Add(ref _writes, writes);
                Interlocked.Increment(ref _packetsSent);
                Interlocked.Add(ref _bytesSent, buffer.Length);
            }
            catch (Exception)
            {
                ReturnBuffer(buffer);
                ReturnQueuedBuffers();
                FireDisconnected(RpcDisconnectReason.WriteError);
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
            FireDisconnected(_receiveDisconnectReason);
        }
    }

    // A test seam. It reads the same deadline as the receive wait.
    internal bool IsIdleExpired()
    {
        if (_idleTimeoutNanos <= 0L)
        {
            return false;
        }

        return !TryGetIdleRemaining(out _);
    }

    // Waits until the socket has data. The wait holds no receive buffer, so an
    // idle connection holds none. A zero-length receive returns when the socket
    // is readable and does not consume the data. Returns false on a socket
    // failure, an idle timeout, or a close.
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "The receive loop treats every socket failure as a disconnect. It is a transport boundary.")]
    private async Task<bool> WaitForDataAsync()
    {
        CancellationTokenSource? idle = null;
        if (_idleTimeoutNanos > 0L)
        {
            if (!TryGetIdleRemaining(out TimeSpan remaining))
            {
                _receiveDisconnectReason = RpcDisconnectReason.IdleTimeout;
                return false;
            }

            idle = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            idle.CancelAfter(remaining);
        }

        CancellationToken token = idle?.Token ?? _lifetime.Token;
        try
        {
            await _socket
                .ReceiveAsync(Memory<byte>.Empty, SocketFlags.None, token)
                .ConfigureAwait(false);
            return true;
        }
        catch (Exception)
        {
            if (idle != null && !_lifetime.IsCancellationRequested && idle.IsCancellationRequested)
            {
                _receiveDisconnectReason = RpcDisconnectReason.IdleTimeout;
            }
            else
            {
                _receiveDisconnectReason = RpcDisconnectReason.ReadError;
            }

            return false;
        }
        finally
        {
            idle?.Dispose();
        }
    }

    // Returns false when the idle deadline has already passed.
    private bool TryGetIdleRemaining(out TimeSpan remaining)
    {
        long idleNanos = _rpcOptions.TimestampNanos() - Volatile.Read(ref _lastReadNanos);
        long remainingNanos = _idleTimeoutNanos - idleNanos;
        if (remainingNanos <= 0L)
        {
            remaining = TimeSpan.Zero;
            return false;
        }

        remaining = ClampDelay(TimeSpan.FromTicks(remainingNanos / 100L));
        return true;
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
                _receiveDisconnectReason = RpcDisconnectReason.ReadError;
                return false;
            }

            if (read == 0)
            {
                _receiveDisconnectReason = RpcDisconnectReason.PeerClosed;
                return false;
            }

            Volatile.Write(ref _lastReadNanos, _rpcOptions.TimestampNanos());
            if (!_decoder.TryPush(buffer.AsSpan(0, read), _onFrame))
            {
                _receiveDisconnectReason = RpcDisconnectReason.ProtocolViolation;
                return false;
            }

            EnsureIdleSweep();
            return true;
        }
        finally
        {
            _receiveBufferPool.Return(buffer, clearArray: true);
        }
    }

    // Shrinks the decoder when it is grown and has been idle for the idle
    // period. The idle sweep calls it. Returns true when the buffer shrank.
    internal bool TryShrinkDecoder()
    {
        long idleNanos = _rpcOptions.TimestampNanos() - Volatile.Read(ref _lastReadNanos);
        if (idleNanos < _idleShrinkAfterNanos)
        {
            return false;
        }

        return _decoder.TryShrink();
    }

    private void EnsureIdleSweep()
    {
        if (_idleShrinkAfterNanos <= 0L)
        {
            return;
        }

        if (_decoder.BufferBytes <= TcpFrameDecoder.StartBufferBytes)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _idleSweepRunning, 1, 0) != 0)
        {
            return;
        }

        _ = IdleSweepLoopAsync();
    }

    // One idle timer per grown decoder. It starts when the decoder first grows
    // above its start size and stops after the shrink, or on the disconnect.
    // A connection that never grows holds no timer.
    private async Task IdleSweepLoopAsync()
    {
        try
        {
            while (_decoder.BufferBytes > TcpFrameDecoder.StartBufferBytes)
            {
                try
                {
                    await Task.Delay(_idleShrinkDelay, _lifetime.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (TryShrinkDecoder())
                {
                    return;
                }
            }
        }
        finally
        {
            Volatile.Write(ref _idleSweepRunning, 0);
        }
    }

    private async Task SendAllAsync(byte[] data, CancellationToken cancellationToken)
    {
        await SendAllAsync(new SendBuffer(data, data.Length, pooled: false), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<int> SendAllAsync(SendBuffer buffer, CancellationToken cancellationToken)
    {
        int offset = 0;
        int writes = 0;
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
            writes++;
        }

        return writes;
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

    private static long DurationNanos(TimeSpan value)
    {
        if (value <= TimeSpan.Zero)
        {
            return 0L;
        }

        try
        {
            return checked(value.Ticks * 100L);
        }
        catch (OverflowException)
        {
            return long.MaxValue;
        }
    }

    // Task.Delay rejects a delay above Int32.MaxValue milliseconds.
    private static TimeSpan ClampDelay(TimeSpan value)
    {
        if (value <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        TimeSpan max = TimeSpan.FromMilliseconds(int.MaxValue);
        return value > max ? max : value;
    }

    private void OnFrame(ReadOnlySpan<byte> frame)
    {
        Interlocked.Increment(ref _packetsReceived);
        Interlocked.Add(ref _bytesReceived, frame.Length);
        _frameReceivedHandlers?.Invoke(frame);
    }

    private void FireDisconnected(RpcDisconnectReason reason)
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

        Action<RpcDisconnectReason>? handlers = _disconnectedHandlers;
        _disconnectedHandlers = null;
        handlers?.Invoke(reason);
    }
}
