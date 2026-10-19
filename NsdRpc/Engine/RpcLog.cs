using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace NsdRpc;

// The library logger. The default is a TraceSource. A peer may route the
// events to its own sink through RpcOptions.LogSink.
internal static class RpcLog
{
    internal static readonly TraceSource Source = new("NsdRpc");

    // True when a warning reaches a sink or a listener. A call site with
    // dynamic text checks this before it builds the text.
    public static bool IsEnabled(Action<TraceEventType, string, Exception?>? sink)
    {
        return sink != null || Source.Switch.ShouldTrace(TraceEventType.Warning);
    }

    // True when an error reaches a sink or a listener.
    public static bool IsErrorEnabled(Action<TraceEventType, string, Exception?>? sink)
    {
        return sink != null || Source.Switch.ShouldTrace(TraceEventType.Error);
    }

    public static void Drop(Action<TraceEventType, string, Exception?>? sink, string reason)
    {
        if (sink != null)
        {
            InvokeSink(sink, TraceEventType.Warning, "Dropped frame: " + reason, null);
            return;
        }

        if (!IsEnabled(null))
        {
            return;
        }

        Source.TraceEvent(TraceEventType.Warning, 0, "Dropped frame: " + reason);
    }

    public static void Error(
        Action<TraceEventType, string, Exception?>? sink, string message, Exception exception)
    {
        if (sink != null)
        {
            InvokeSink(sink, TraceEventType.Error, message, exception);
            return;
        }

        if (!IsErrorEnabled(null))
        {
            return;
        }

        Source.TraceEvent(TraceEventType.Error, 0, message + Environment.NewLine + exception);
    }

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "A logging sink failure must not kill the transport thread.")]
    private static void InvokeSink(
        Action<TraceEventType, string, Exception?> sink,
        TraceEventType level,
        string message,
        Exception? exception)
    {
        try
        {
            sink(level, message, exception);
        }
        catch (Exception)
        {
            // A sink failure must not kill the transport thread.
        }
    }
}
