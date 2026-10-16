namespace NsdRpc.Benchmarks;

// The method ids the benchmark handlers register. The values are arbitrary.
internal static class BenchmarkMethods
{
    public const ulong EchoHandshake = 1001UL;

    public const ulong StreamHandshake = 1002UL;

    public const ulong EchoBlob = 1003UL;
}
