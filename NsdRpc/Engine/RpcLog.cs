using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace NsdRpc;

// The library logger. It uses a TraceSource so consumers attach listeners
// without a logging dependency.
internal static class RpcLog
{
    internal static readonly TraceSource Source = new("NsdRpc");

    // True when a warning reaches a listener. A call site with dynamic text
    // checks this before it builds the text.
    public static bool IsEnabled => Source.Switch.ShouldTrace(TraceEventType.Warning);

    // True when an error reaches a listener.
    public static bool IsErrorEnabled => Source.Switch.ShouldTrace(TraceEventType.Error);

    [SuppressMessage(
        "Globalization",
        "CA1303",
        Justification = "Log strings are developer diagnostics, not localized UI.")]
    public static void Drop(string reason)
    {
        if (!IsEnabled)
        {
            return;
        }

        Source.TraceEvent(TraceEventType.Warning, 0, "Dropped frame: " + reason);
    }

    [SuppressMessage(
        "Globalization",
        "CA1303",
        Justification = "Log strings are developer diagnostics, not localized UI.")]
    public static void Error(string message, Exception exception)
    {
        if (!IsErrorEnabled)
        {
            return;
        }

        Source.TraceEvent(TraceEventType.Error, 0, message + Environment.NewLine + exception);
    }
}
