using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace NsdRpc;

// The library logger. It uses a TraceSource so consumers attach listeners
// without a logging dependency.
internal static class RpcLog
{
    private static readonly TraceSource Source = new("NsdRpc");

    [SuppressMessage(
        "Globalization",
        "CA1303",
        Justification = "Log strings are developer diagnostics, not localized UI.")]
    public static void Drop(string reason)
    {
        Source.TraceEvent(TraceEventType.Warning, 0, "Dropped frame: " + reason);
    }

    [SuppressMessage(
        "Globalization",
        "CA1303",
        Justification = "Log strings are developer diagnostics, not localized UI.")]
    public static void Error(string message, Exception exception)
    {
        Source.TraceEvent(TraceEventType.Error, 0, message + Environment.NewLine + exception);
    }
}
