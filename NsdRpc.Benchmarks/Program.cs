using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace NsdRpc.Benchmarks;

internal static class Program
{
    public static int Main(string[] args)
    {
        IEnumerable<Summary> summaries =
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        return summaries.Any(summary => summary.HasCriticalValidationErrors) ? 1 : 0;
    }
}
