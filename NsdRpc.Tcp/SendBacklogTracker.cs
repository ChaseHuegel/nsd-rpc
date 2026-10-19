namespace NsdRpc.Tcp;

// Tracks how long the send queue has stayed at or above its liveness
// threshold. The connection feeds it the queue depth at each enqueue and
// drain. It holds no timer.
internal sealed class SendBacklogTracker
{
    private readonly int _thresholdBytes;
    private readonly long _windowNanos;
    private long _sinceNanos = -1L;

    public SendBacklogTracker(int thresholdBytes, int windowMs)
    {
        _thresholdBytes = thresholdBytes;
        _windowNanos = windowMs * 1_000_000L;
    }

    // Returns true when the queue stayed at or above the threshold for the
    // whole window. A drop below the threshold resets the window.
    public bool ExceedsWindow(int queuedBytes, long nowNanos)
    {
        if (_thresholdBytes <= 0)
        {
            return false;
        }

        if (queuedBytes < _thresholdBytes)
        {
            _sinceNanos = -1L;
            return false;
        }

        if (_sinceNanos < 0L)
        {
            _sinceNanos = nowNanos;
            return false;
        }

        return nowNanos - _sinceNanos >= _windowNanos;
    }
}
