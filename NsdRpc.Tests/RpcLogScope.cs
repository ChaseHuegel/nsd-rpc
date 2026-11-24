using System.Diagnostics;

namespace NsdRpc.Tests;

// A test scope for the process-global RpcLog trace source. It restores the
// previous switch level and listener set when the scope ends.
internal sealed class RpcLogScope : IDisposable
{
    private readonly SourceLevels _previousLevel;
    private readonly TraceListener[] _previousListeners;

    private RpcLogScope(SourceLevels level, TraceListener? listener)
    {
        _previousLevel = RpcLog.Source.Switch.Level;
        _previousListeners = SnapshotListeners();
        RpcLog.Source.Switch.Level = level;
        if (listener != null)
        {
            RpcLog.Source.Listeners.Add(listener);
        }
    }

    public static RpcLogScope WithLevel(SourceLevels level)
    {
        return new RpcLogScope(level, null);
    }

    public static RpcLogScope WithLevelAndListener(SourceLevels level, TraceListener listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        return new RpcLogScope(level, listener);
    }

    public void Dispose()
    {
        RpcLog.Source.Listeners.Clear();
        RpcLog.Source.Listeners.AddRange(_previousListeners);
        RpcLog.Source.Switch.Level = _previousLevel;
        GC.SuppressFinalize(this);
    }

    private static TraceListener[] SnapshotListeners()
    {
        var listeners = new TraceListener[RpcLog.Source.Listeners.Count];
        int index = 0;
        foreach (TraceListener listener in RpcLog.Source.Listeners)
        {
            listeners[index] = listener;
            index++;
        }

        return listeners;
    }
}
