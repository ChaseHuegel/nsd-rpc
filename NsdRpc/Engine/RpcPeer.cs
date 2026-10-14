using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using NsdRpc.Wire;

namespace NsdRpc;

/// <summary>
/// One RPC peer over one connection. Owns the envelope, the id space, and the
/// pending calls. It knows nothing about handlers.
/// </summary>
public sealed class RpcPeer
{
    private const byte RpcVersion = 1;

    private readonly IRpcConnection _connection;
    private readonly RpcOptions _options;
    private readonly Func<long> _timestampNanos;
    private readonly bool _isServer;
    private readonly ulong _clientId;

    private readonly object _pendingLock = new();
    private readonly Dictionary<ulong, PendingCallBase> _pending = new();
    private readonly PendingCallBase[] _expiredScratch;
    private ulong _nextId = 1;
    private bool _idOverflowed;

    private readonly object _inboundLock = new();
    private readonly Dictionary<ulong, InboundCall> _inbound = new();
    private readonly Queue<DispatchEntry> _dispatchQueue = new();

    private readonly SemaphoreSlim _dispatchSignal = new(0);
    private readonly object _workerLock = new();
    private Task? _workerTask;
    private bool _workerRunning;

    private readonly CancellationTokenSource _connectionToken = new();
    private volatile bool _faulted;
    private RpcDispatchHook? _dispatchHook;

    public RpcPeer(IRpcConnection connection, RpcOptions? options = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _options = options ?? RpcOptions.Default;
        ValidateOptions();
        _timestampNanos = _options.TimestampNanos;
        _isServer = connection is IServerConnection;
        _clientId = (connection as IServerConnection)?.ClientId ?? 0UL;
        _expiredScratch = new PendingCallBase[_options.MaxPendingCalls];
        _connection.Disconnected += OnDisconnected;
    }

    internal IRpcConnection Connection => _connection;

    internal ulong ClientId => _clientId;

    internal bool IsServer => _isServer;

    internal CancellationToken ConnectionToken => _connectionToken.Token;

    internal RpcDispatchHook? DispatchHook
    {
        get => _dispatchHook;
        set => _dispatchHook = value;
    }

    public Task<RpcResult<TResponse>> InvokeAsync<TRequest, TResponse>(
        ulong method,
        in TRequest request,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where TRequest : struct
        where TResponse : struct
    {
        if (method == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(method), "Method zero is not a valid method id.");
        }

        long timeoutNanos = ComputeTimeoutNanos(timeout);
        long deadlineNanos = ComputeDeadline(timeoutNanos);

        if (!TryAllocateId(out ulong id, out int rejectCode, out string rejectMessage))
        {
            if (rejectCode == (int)RpcErrorCode.InternalError)
            {
                FaultPending(rejectCode, rejectMessage);
            }

            return FailedTask<TResponse>(rejectCode, rejectMessage);
        }

        byte[]? payload = SerializePayload(in request, PayloadSerializerCache<TRequest>.Value);
        if (payload != null && FrameSizeFor(payload.Length) > _options.MaxFrameBytes)
        {
            return FailedTask<TResponse>((int)RpcErrorCode.InvalidRequest, "the request frame exceeds MaxFrameBytes");
        }

        var pending = new PendingCall<TResponse>(
            id, deadlineNanos, timeoutNanos, PayloadSerializerCache<TResponse>.Value);
        if (!TryAddPending(pending))
        {
            pending.Fail((int)RpcErrorCode.Unavailable, "peer faulted");
            return pending.Completion.Task;
        }

        if (cancellationToken.CanBeCanceled)
        {
            pending.Registration = cancellationToken.Register(OnCallCancelled, pending);
        }

        if (IsPending(pending.Id))
        {
            SendRequestFrame(pending.Id, method, payload, pending);
        }

        return pending.Completion.Task;
    }

    public Task<RpcResult<TResponse>> InvokeStreamAsync<TRequest, TChunk, TResponse>(
        ulong method,
        in TRequest request,
        IChunkSink<TChunk> sink,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where TRequest : struct
        where TChunk : struct
        where TResponse : struct
    {
        if (method == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(method), "Method zero is not a valid method id.");
        }

        if (sink == null)
        {
            throw new ArgumentNullException(nameof(sink));
        }

        long timeoutNanos = ComputeTimeoutNanos(timeout);
        long deadlineNanos = ComputeDeadline(timeoutNanos);

        if (!TryAllocateId(out ulong id, out int rejectCode, out string rejectMessage))
        {
            if (rejectCode == (int)RpcErrorCode.InternalError)
            {
                FaultPending(rejectCode, rejectMessage);
            }

            return FailedTask<TResponse>(rejectCode, rejectMessage);
        }

        byte[]? payload = SerializePayload(in request, PayloadSerializerCache<TRequest>.Value);
        if (payload != null && FrameSizeFor(payload.Length) > _options.MaxFrameBytes)
        {
            return FailedTask<TResponse>((int)RpcErrorCode.InvalidRequest, "the request frame exceeds MaxFrameBytes");
        }

        var pending = new PendingStreamCall<TChunk, TResponse>(
            id,
            deadlineNanos,
            timeoutNanos,
            PayloadSerializerCache<TResponse>.Value,
            PayloadSerializerCache<TChunk>.Value,
            sink);
        if (!TryAddPending(pending))
        {
            pending.Fail((int)RpcErrorCode.Unavailable, "peer faulted");
            return pending.Completion.Task;
        }

        if (cancellationToken.CanBeCanceled)
        {
            pending.Registration = cancellationToken.Register(OnCallCancelled, pending);
        }

        if (IsPending(pending.Id))
        {
            SendRequestFrame(pending.Id, method, payload, pending);
        }

        return pending.Completion.Task;
    }

    public bool Notify<TRequest>(ulong method, in TRequest request)
        where TRequest : struct
    {
        if (method == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(method), "Method zero is not a valid method id.");
        }

        if (_faulted)
        {
            return false;
        }

        byte[]? payload = SerializePayload(in request, PayloadSerializerCache<TRequest>.Value);
        return SendNotificationFrame(method, payload);
    }

    public bool Notify(ulong method)
    {
        if (method == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(method), "Method zero is not a valid method id.");
        }

        if (_faulted)
        {
            return false;
        }

        return SendNotificationFrame(method, null);
    }

    public void HandleFrame(ReadOnlySpan<byte> frame)
    {
        if (_faulted)
        {
            return;
        }

        if (frame.Length > _options.MaxFrameBytes)
        {
            RpcLog.Drop("frame exceeds MaxFrameBytes");
            return;
        }

        Envelope envelope;
        try
        {
            envelope = Envelope.Deserialize(frame);
        }
        catch (InvalidDataException)
        {
            RpcLog.Drop("undecodable envelope");
            return;
        }

        if (envelope.Version != RpcVersion)
        {
            RpcLog.Drop("unknown protocol version");
            FaultPending((int)RpcErrorCode.Unavailable, "protocol version mismatch");
            return;
        }

        switch ((RpcMessageKind)envelope.Kind)
        {
            case RpcMessageKind.Request:
                HandleRequest(in envelope);
                break;
            case RpcMessageKind.Response:
                HandleResponse(in envelope);
                break;
            case RpcMessageKind.Notification:
                HandleNotification(in envelope);
                break;
            case RpcMessageKind.Cancellation:
                HandleCancellation(in envelope);
                break;
            default:
                RpcLog.Drop("unknown message kind " + envelope.Kind);
                break;
        }
    }

    public void CheckTimeouts(long timestampNanos)
    {
        int count = 0;
        lock (_pendingLock)
        {
            foreach (KeyValuePair<ulong, PendingCallBase> pair in _pending)
            {
                if (pair.Value.DeadlineNanos > timestampNanos)
                {
                    continue;
                }

                _expiredScratch[count] = pair.Value;
                count++;
            }

            Invariant.That(count <= _options.MaxPendingCalls, "The pending table exceeds its bound.");

            for (int i = 0; i < count; i++)
            {
                _pending.Remove(_expiredScratch[i].Id);
            }
        }

        for (int i = 0; i < count; i++)
        {
            PendingCallBase pending = _expiredScratch[i];
            pending.DisposeRegistration();
            SendCancellationFrame(pending.Id);
            pending.Fail((int)RpcErrorCode.RequestTimeout, "request timed out");
        }
    }

    public void Poll()
    {
        CheckTimeouts(_timestampNanos());
    }

    public void FaultPending(int errorCode, string message)
    {
        _faulted = true;
        _connectionToken.Cancel();

        PendingCallBase[] snapshot = ArrayPool<PendingCallBase>.Shared.Rent(_options.MaxPendingCalls);
        int count = 0;
        lock (_pendingLock)
        {
            foreach (KeyValuePair<ulong, PendingCallBase> pair in _pending)
            {
                snapshot[count] = pair.Value;
                count++;
            }

            _pending.Clear();
        }

        lock (_inboundLock)
        {
            _dispatchQueue.Clear();
            _inbound.Clear();
        }

        for (int i = 0; i < count; i++)
        {
            PendingCallBase pending = snapshot[i];
            pending.DisposeRegistration();
            pending.Fail(errorCode, message);
        }

        ArrayPool<PendingCallBase>.Shared.Return(snapshot, clearArray: false);
        _dispatchSignal.Release();
    }

    internal void RespondAsserting<TResponse>(ulong requestId, in RpcResult<TResponse> result)
        where TResponse : struct
    {
        switch (ClaimInbound(requestId))
        {
            case InboundClaim.Claimed:
                SendResponseFrame(requestId, in result);
                return;
            case InboundClaim.Cancelled:
                RpcLog.Drop("response after cancellation");
                return;
            case InboundClaim.Faulted:
                RpcLog.Drop("response after fault");
                return;
            default:
                Invariant.Fail("response already sent for request " + requestId);
                return;
        }
    }

    internal void TryRespond<TResponse>(ulong requestId, in RpcResult<TResponse> result)
        where TResponse : struct
    {
        if (ClaimInbound(requestId) == InboundClaim.Claimed)
        {
            SendResponseFrame(requestId, in result);
        }
    }

    internal void TryFail(ulong requestId, int code, string message)
    {
        if (ClaimInbound(requestId) == InboundClaim.Claimed)
        {
            SendErrorResponseFrame(requestId, code, message);
        }
    }

    internal void FailAsserting(ulong requestId, RpcErrorCode code, string message)
    {
        switch (ClaimInbound(requestId))
        {
            case InboundClaim.Claimed:
                SendErrorResponseFrame(requestId, (int)code, message);
                return;
            case InboundClaim.Cancelled:
            case InboundClaim.Faulted:
                RpcLog.Drop("error response after cancellation or fault");
                return;
            default:
                Invariant.Fail("response already sent for request " + requestId);
                return;
        }
    }

    internal void StreamAsserting<TChunk>(ulong method, ulong requestId, in TChunk chunk)
        where TChunk : struct
    {
        InboundStreamState state;
        lock (_inboundLock)
        {
            if (_faulted)
            {
                state = InboundStreamState.Faulted;
            }
            else if (!_inbound.TryGetValue(requestId, out InboundCall? inbound))
            {
                state = InboundStreamState.Absent;
            }
            else
            {
                state = inbound.Cancelled ? InboundStreamState.Cancelled : InboundStreamState.Open;
            }
        }

        if (state == InboundStreamState.Faulted)
        {
            RpcLog.Drop("chunk after fault");
            return;
        }

        if (state == InboundStreamState.Absent)
        {
            Invariant.Fail("chunk after response for request " + requestId);
            return;
        }

        if (state == InboundStreamState.Cancelled)
        {
            RpcLog.Drop("chunk after cancellation");
            return;
        }

        SendChunkFrame(method, requestId, in chunk);
    }

    internal void AttachCancellation(ulong requestId, CancellationTokenSource cancelSource)
    {
        lock (_inboundLock)
        {
            if (_inbound.TryGetValue(requestId, out InboundCall? inbound))
            {
                inbound.CancelSource = cancelSource;
            }
        }
    }

    private void ValidateOptions()
    {
        if (_options.MaxFrameBytes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(_options), "MaxFrameBytes must be positive.");
        }

        if (_options.MaxPendingCalls < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(_options), "MaxPendingCalls must be positive.");
        }

        if (_options.MaxBufferedChunks < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(_options), "MaxBufferedChunks must be positive.");
        }

        if (_options.Timeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(_options), "Timeout must not be negative.");
        }
    }

    private bool TryAllocateId(out ulong id, out int rejectCode, out string rejectMessage)
    {
        lock (_pendingLock)
        {
            if (_faulted)
            {
                id = 0UL;
                rejectCode = (int)RpcErrorCode.Unavailable;
                rejectMessage = "peer faulted";
                return false;
            }

            if (_idOverflowed)
            {
                id = 0UL;
                rejectCode = (int)RpcErrorCode.InternalError;
                rejectMessage = "request id space exhausted";
                return false;
            }

            if (_pending.Count >= _options.MaxPendingCalls)
            {
                id = 0UL;
                rejectCode = (int)RpcErrorCode.Busy;
                rejectMessage = "too many pending calls";
                return false;
            }

            try
            {
                id = checked(_nextId++);
            }
            catch (OverflowException)
            {
                _idOverflowed = true;
                id = 0UL;
                rejectCode = (int)RpcErrorCode.InternalError;
                rejectMessage = "request id space exhausted";
                return false;
            }

            rejectCode = 0;
            rejectMessage = string.Empty;
            return true;
        }
    }

    private bool TryAddPending(PendingCallBase pending)
    {
        lock (_pendingLock)
        {
            if (_faulted)
            {
                return false;
            }

            _pending.Add(pending.Id, pending);
            return true;
        }
    }

    private bool IsPending(ulong id)
    {
        lock (_pendingLock)
        {
            return _pending.ContainsKey(id);
        }
    }

    private void OnCallCancelled(object? state)
    {
        var pending = (PendingCallBase)state!;
        lock (_pendingLock)
        {
            if (!_pending.Remove(pending.Id))
            {
                return;
            }
        }

        pending.DisposeRegistration();
        SendCancellationFrame(pending.Id);
        pending.Fail((int)RpcErrorCode.Cancelled, "cancelled");
    }

    private long ComputeTimeoutNanos(TimeSpan? timeout)
    {
        TimeSpan value = timeout ?? _options.Timeout;
        if (value == TimeSpan.Zero)
        {
            return 0L;
        }

        if (value < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "The timeout must not be negative.");
        }

        try
        {
            return checked(value.Ticks * 100L);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "The timeout is too large.");
        }
    }

    private long ComputeDeadline(long timeoutNanos)
    {
        if (timeoutNanos == 0L)
        {
            return long.MaxValue;
        }

        long now = _timestampNanos();
        try
        {
            return checked(now + timeoutNanos);
        }
        catch (OverflowException)
        {
            return long.MaxValue;
        }
    }

    private static int FrameSizeFor(int payloadLength)
    {
        // 26 required envelope bytes plus 5 bytes of payload field overhead.
        return checked(26 + 5 + payloadLength);
    }

    private static Task<RpcResult<T>> FailedTask<T>(int errorCode, string errorMessage)
    {
        return Task.FromResult(RpcResult<T>.Failure(errorCode, errorMessage));
    }

    private static byte[]? SerializePayload<T>(in T value, IPayloadSerializer<T> serializer)
        where T : struct
    {
        int size = serializer.GetSize(in value);
        if (size == 0)
        {
            return null;
        }

        byte[] buffer = new byte[size];
        serializer.Serialize(in value, ref buffer);
        return buffer;
    }

    private bool SendEnvelope(in Envelope envelope)
    {
        int size = envelope.GetSize();
        if (size > _options.MaxFrameBytes)
        {
            RpcLog.Drop("frame exceeds MaxFrameBytes");
            return false;
        }

        byte[] frame = ArrayPool<byte>.Shared.Rent(size);
        try
        {
            envelope.SerializeInto(frame.AsSpan(0, size));
            return _connection.TrySend(frame.AsSpan(0, size));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(frame, clearArray: true);
        }
    }

    private void SendRequestFrame(ulong id, ulong method, byte[]? payload, PendingCallBase pending)
    {
        var envelope = new Envelope(RpcVersion, (byte)RpcMessageKind.Request, id, method, payload, null);
        if (!SendEnvelope(in envelope))
        {
            RemoveAndFail(pending, (int)RpcErrorCode.Unavailable, "send rejected");
        }
    }

    private bool SendNotificationFrame(ulong method, byte[]? payload)
    {
        var envelope = new Envelope(RpcVersion, (byte)RpcMessageKind.Notification, 0UL, method, payload, null);
        return SendEnvelope(in envelope);
    }

    private void SendResponseFrame<TResponse>(ulong requestId, in RpcResult<TResponse> result)
        where TResponse : struct
    {
        byte[]? payload = null;
        RpcError? error = null;
        if (result.IsSuccess)
        {
            TResponse value = result.Value;
            payload = SerializePayload(in value, PayloadSerializerCache<TResponse>.Value);
        }
        else
        {
            error = new RpcError(result.ErrorCode, result.ErrorMessage);
        }

        var envelope = new Envelope(RpcVersion, (byte)RpcMessageKind.Response, requestId, 0UL, payload, error);
        if (!SendEnvelope(in envelope))
        {
            RpcLog.Drop("response send rejected");
        }
    }

    private void SendChunkFrame<TChunk>(ulong method, ulong requestId, in TChunk chunk)
        where TChunk : struct
    {
        byte[]? payload = SerializePayload(in chunk, PayloadSerializerCache<TChunk>.Value);
        var envelope = new Envelope(
            RpcVersion, (byte)RpcMessageKind.Notification, requestId, method, payload, null);
        if (!SendEnvelope(in envelope))
        {
            RpcLog.Drop("chunk send rejected");
        }
    }

    private void SendCancellationFrame(ulong id)
    {
        var envelope = new Envelope(RpcVersion, (byte)RpcMessageKind.Cancellation, id, 0UL, null, null);
        if (!SendEnvelope(in envelope))
        {
            RpcLog.Drop("cancellation send rejected");
        }
    }

    private void SendErrorResponseFrame(ulong requestId, int code, string message)
    {
        var error = new RpcError(code, message);
        var envelope = new Envelope(RpcVersion, (byte)RpcMessageKind.Response, requestId, 0UL, null, error);
        if (!SendEnvelope(in envelope))
        {
            RpcLog.Drop("error response send rejected");
        }
    }

    private void RemoveAndFail(PendingCallBase pending, int errorCode, string errorMessage)
    {
        lock (_pendingLock)
        {
            if (!_pending.Remove(pending.Id))
            {
                return;
            }
        }

        pending.DisposeRegistration();
        pending.Fail(errorCode, errorMessage);
    }

    private void HandleRequest(in Envelope envelope)
    {
        if (envelope.Id == 0UL)
        {
            RpcLog.Drop("request without id");
            return;
        }

        if (envelope.Method == 0UL)
        {
            RpcLog.Drop("request without method");
            return;
        }

        if (envelope.Error != null)
        {
            RpcLog.Drop("request with error");
            return;
        }

        bool duplicate = false;
        bool busy = false;
        lock (_inboundLock)
        {
            if (_inbound.ContainsKey(envelope.Id))
            {
                duplicate = true;
            }
            else if (_inbound.Count >= _options.MaxPendingCalls)
            {
                busy = true;
            }
            else
            {
                var inbound = new InboundCall(envelope.Id, envelope.Method);
                _inbound.Add(envelope.Id, inbound);
                _dispatchQueue.Enqueue(new DispatchEntry(inbound, envelope.Method, envelope.Payload));
            }
        }

        if (duplicate)
        {
            RpcLog.Drop("duplicate request id");
            return;
        }

        if (busy)
        {
            SendErrorResponseFrame(envelope.Id, (int)RpcErrorCode.Busy, "busy");
            return;
        }

        EnsureWorkerRunning();
        _dispatchSignal.Release();
    }

    private void HandleResponse(in Envelope envelope)
    {
        if (envelope.Id == 0UL)
        {
            RpcLog.Drop("response without id");
            return;
        }

        if (envelope.Method != 0UL)
        {
            RpcLog.Drop("response with method");
            return;
        }

        PendingCallBase? pending;
        lock (_pendingLock)
        {
            if (!_pending.TryGetValue(envelope.Id, out pending))
            {
                RpcLog.Drop("unknown response id");
                return;
            }

            _pending.Remove(envelope.Id);
        }

        pending.DisposeRegistration();
        if (envelope.Error != null)
        {
            pending.Fail(envelope.Error.Value.Code, envelope.Error.Value.Message);
        }
        else
        {
            pending.CompleteResult(envelope.Payload);
        }
    }

    private void HandleNotification(in Envelope envelope)
    {
        if (envelope.Id == 0UL)
        {
            HandleStandaloneNotification(in envelope);
            return;
        }

        HandleChunk(in envelope);
    }

    private void HandleStandaloneNotification(in Envelope envelope)
    {
        if (envelope.Method == 0UL)
        {
            RpcLog.Drop("notification without method");
            return;
        }

        if (envelope.Error != null)
        {
            RpcLog.Drop("notification with error");
            return;
        }

        lock (_inboundLock)
        {
            _dispatchQueue.Enqueue(new DispatchEntry(null, envelope.Method, envelope.Payload));
        }

        EnsureWorkerRunning();
        _dispatchSignal.Release();
    }

    private void HandleChunk(in Envelope envelope)
    {
        if (envelope.Method == 0UL)
        {
            RpcLog.Drop("chunk without method");
            return;
        }

        if (envelope.Error != null)
        {
            RpcLog.Drop("chunk with error");
            return;
        }

        long now = _timestampNanos();
        PendingCallBase? pending;
        lock (_pendingLock)
        {
            if (!_pending.TryGetValue(envelope.Id, out pending))
            {
                RpcLog.Drop("unknown chunk id");
                return;
            }

            pending.DeadlineNanos = ResetDeadline(pending.TimeoutNanos, now);
        }

        if (!pending.ReceiveChunk(envelope.Payload))
        {
            RpcLog.Drop("chunk for a non-stream call");
        }
    }

    private void HandleCancellation(in Envelope envelope)
    {
        if (envelope.Id == 0UL)
        {
            RpcLog.Drop("cancellation without id");
            return;
        }

        if (envelope.Method != 0UL)
        {
            RpcLog.Drop("cancellation with method");
            return;
        }

        if (envelope.Payload != null)
        {
            RpcLog.Drop("cancellation with payload");
            return;
        }

        if (envelope.Error != null)
        {
            RpcLog.Drop("cancellation with error");
            return;
        }

        CancellationTokenSource? cancelSource = null;
        lock (_inboundLock)
        {
            if (_inbound.TryGetValue(envelope.Id, out InboundCall? inbound))
            {
                inbound.Cancelled = true;
                cancelSource = inbound.CancelSource;
            }
        }

        if (cancelSource == null)
        {
            RpcLog.Drop("cancellation with unknown id");
            return;
        }

        cancelSource.Cancel();
    }

    private long ResetDeadline(long timeoutNanos, long nowNanos)
    {
        if (timeoutNanos == 0L)
        {
            return long.MaxValue;
        }

        try
        {
            return checked(nowNanos + timeoutNanos);
        }
        catch (OverflowException)
        {
            return long.MaxValue;
        }
    }

    private void EnsureWorkerRunning()
    {
        lock (_workerLock)
        {
            if (_workerRunning)
            {
                return;
            }

            _workerRunning = true;
            _workerTask = Task.Run(WorkerLoopAsync);
        }
    }

    private async Task WorkerLoopAsync()
    {
        while (true)
        {
            await _dispatchSignal.WaitAsync().ConfigureAwait(false);
            if (_faulted)
            {
                return;
            }

            await DrainDispatchQueueAsync().ConfigureAwait(false);
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "The dispatch boundary catches every handler error. It is the one allowed catch-all.")]
    private async Task DrainDispatchQueueAsync()
    {
        while (true)
        {
            DispatchEntry entry;
            lock (_inboundLock)
            {
                if (_dispatchQueue.Count == 0)
                {
                    return;
                }

                entry = _dispatchQueue.Dequeue();
            }

            if (_faulted)
            {
                return;
            }

            if (entry.Inbound != null && entry.Inbound.Cancelled)
            {
                RpcLog.Drop("cancelled before dispatch");
                continue;
            }

            RpcDispatchHook? hook = _dispatchHook;
            if (hook == null)
            {
                if (entry.Inbound != null)
                {
                    SendErrorResponseFrame(
                        entry.Inbound.RequestId, (int)RpcErrorCode.MethodNotFound, "no handler registered");
                }

                continue;
            }

            var context = new RpcContext(this, entry.Inbound?.RequestId ?? 0UL, entry.Method);
            try
            {
                ValueTask task = hook(context, entry.Payload);
                if (!task.IsCompletedSuccessfully)
                {
                    await task.ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                RpcLog.Error("dispatch hook failed", exception);
                if (entry.Inbound != null)
                {
                    SendErrorResponseFrame(entry.Inbound.RequestId, (int)RpcErrorCode.InternalError, "internal error");
                }
            }
        }
    }

    private void OnDisconnected()
    {
        FaultPending((int)RpcErrorCode.Unavailable, "connection closed");
    }

    private enum InboundClaim
    {
        Claimed,
        Absent,
        Cancelled,
        Faulted,
    }

    private enum InboundStreamState
    {
        Open,
        Absent,
        Cancelled,
        Faulted,
    }

    private InboundClaim ClaimInbound(ulong requestId)
    {
        lock (_inboundLock)
        {
            if (_faulted)
            {
                return InboundClaim.Faulted;
            }

            if (!_inbound.TryGetValue(requestId, out InboundCall? inbound))
            {
                return InboundClaim.Absent;
            }

            if (inbound.Cancelled)
            {
                return InboundClaim.Cancelled;
            }

            _inbound.Remove(requestId);
            return InboundClaim.Claimed;
        }
    }
}
