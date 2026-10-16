# Performance Baselines

One subject: how to measure the performance and the memory of the engine
and the TCP transport.

The benchmark project is
[NsdRpc.Benchmarks](../NsdRpc.Benchmarks/NsdRpc.Benchmarks.csproj). It uses
BenchmarkDotNet. It is not packed and it does not ship.

## Run

```bash
dotnet run --project NsdRpc.Benchmarks -c Release -- --filter '*'
```

Without `--filter`, BenchmarkDotNet asks for a selection on the console.
Use `--filter` in scripts. The run writes reports to
`BenchmarkDotNet.Artifacts/`.

## What is measured

The suite covers the paths the Backlog performance issues touch.

| Benchmark | Path |
|---|---|
| `EnvelopeCodecBenchmarks` | The envelope codec at 64 bytes and at the 65,535-byte payload cap |
| `InvokeRoundTripBenchmarks` | One request and one response over the engine |
| `StreamRoundTripBenchmarks` | One stream request, ten chunks, and one response |
| `TimeoutScanBenchmarks` | One poll over a full pending table |
| `DropPathBenchmarks` | One dropped frame with an unknown kind |
| `PeerConstructionBenchmarks` | One peer construction |
| `TcpRoundTripBenchmarks` | One round trip over loopback at 4 bytes and 4 kilobytes |

The in-memory benchmarks use a synchronous connection. The engine path is
the measured work. The TCP benchmarks use loopback sockets. The peer
construction benchmark includes one small connection object in the
measurement.

## Reading the results

- The `Allocated` column names the managed bytes per operation. It does not
  show pooled buffer churn or native memory.
- Compare runs on one machine. Absolute times vary across machines.
- Run the tests too. The allocation tests in [testing](testing.md) prove
  the allocation counts. The benchmarks record the size.

The ticket list lives in [issues/index](issues/index.md). Record each
before-and-after measurement in the matching issue.

## Baseline

First run: 2026-10-15, library commit `2690e9a`, BenchmarkDotNet 0.15.2.

Machine: AMD Ryzen 5 3600X 4.41 GHz, 6 physical cores, 12 logical cores,
Ubuntu 26.04.1, .NET 10.0.12, X64 RyuJIT AVX2, concurrent workstation GC.

| Benchmark | Mean | Allocated |
|---|---:|---:|
| `EnvelopeCodecBenchmarks.SerializeSmallPayload` | 44.15 ns | 0 B |
| `EnvelopeCodecBenchmarks.SerializeMaximumPayload` | 48.58 μs | 0 B |
| `EnvelopeCodecBenchmarks.DeserializeSmallPayload` | 94.96 ns | 88 B |
| `EnvelopeCodecBenchmarks.DeserializeMaximumPayload` | 84.47 μs | 65,560 B |
| `InvokeRoundTripBenchmarks.InvokeRoundTrip` | 3.938 μs | 624 B |
| `StreamRoundTripBenchmarks.StreamRoundTrip` | 12.00 μs | 2.53 KB |
| `TimeoutScanBenchmarks.CheckTimeoutsBelowEveryDeadline` | 1.227 μs | 0 B |
| `DropPathBenchmarks.HandleUnknownKind` | 52.25 ns | 168 B |
| `PeerConstructionBenchmarks.ConstructDefaultPeer` | 453.4 ns | 8.73 KB |
| `TcpRoundTripBenchmarks.RoundTripHandshake` | 50.67 μs | 945 B |
| `TcpRoundTripBenchmarks.RoundTripBlob` | 92.56 μs | 33,774 B |

The two maximum-payload codec rows show the byte copy loop from issue
[0027](issues/0027-generated-payload-memcpy.md). The drop row shows the log
message allocation from issue [0045](issues/0045-trace-switch-guard.md).
The peer construction row shows the scratch array from issue
[0028](issues/0028-lazy-peer-timeout-scratch.md). Compare later runs on the
same machine against this table.
