using System.Diagnostics;

namespace NsdRpc;

// A monotonic timestamp source in nanoseconds. The default clock. Tests
// inject a fake through RpcOptions.TimestampNanos.
internal static class RpcTime
{
    private static readonly double NanosPerTick = 1_000_000_000.0 / Stopwatch.Frequency;

    public static long NowNanos()
    {
        return checked((long)(Stopwatch.GetTimestamp() * NanosPerTick));
    }
}
