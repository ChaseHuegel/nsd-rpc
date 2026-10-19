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
| `TcpSendBatchBenchmarks` | 4,096 small frames over loopback with and without a 1 ms send batch interval |

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

First run: 2026-10-15, library commit `22e9bae`, BenchmarkDotNet 0.15.2.

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

## Second run

Second run: 2026-10-16, commit `20b14e4`, the same machine and
BenchmarkDotNet version. Every Active Sprint performance issue is fixed.

| Benchmark | Baseline | Second run | Change |
|---|---:|---:|---:|
| `SerializeSmallPayload` | 44.15 ns | 45.13 ns | +2% |
| `SerializeMaximumPayload` | 48.58 μs | 47.95 μs | −1% |
| `DeserializeSmallPayload` | 94.96 ns / 88 B | 87.71 ns / 88 B | −8% |
| `DeserializeMaximumPayload` | 84.47 μs / 65,560 B | 69.57 μs / 65,560 B | Within the baseline spread |
| `InvokeRoundTrip` | 3.938 μs / 624 B | 3.019 μs / 616 B | −23% |
| `StreamRoundTrip` | 12.00 μs / 2.53 KB | 6.987 μs / 2.16 KB | −42% |
| `CheckTimeoutsBelowEveryDeadline` | 1.227 μs | 1.201 ns | −99.9% |
| `HandleUnknownKind` | 52.25 ns / 168 B | 19.95 ns / 0 B | −62% |
| `ConstructDefaultPeer` | 453.4 ns / 8.73 KB | 89.45 ns / 800 B | −91% bytes |
| `RoundTripHandshake` | 50.67 μs / 945 B | 39.11 μs / 677 B | −23% |
| `RoundTripBlob` | 92.56 μs / 33,774 B | 69.82 μs / 25,320 B | −25% |

The maximum-payload codec is unchanged. Issue
[0027](issues/0027-generated-payload-memcpy.md) tracks the upstream copy
loop, so its two rows differ by less than the baseline spread.

## Send batching

Run: 2026-10-19, `--filter '*TcpSendBatch*'`, the same machine. The benchmark
sends 4,096 eight-byte frames over loopback and reads the whole stream.

| Benchmark | Mean | Allocated | Socket writes |
|---|---:|---:|---:|
| `TcpSendBatchBenchmarks.SendImmediately` | 27.98 ms | 141.86 KB | 4,096 |
| `TcpSendBatchBenchmarks.SendBatched` (1 ms interval) | 1.219 ms | 129.02 KB | 1 |

The batched path is 23 times faster. The interval costs one millisecond per
batch. The write counts come from the batch tests, which pin one write for a
full batch. The benchmark records the throughput.

The send path dropped about 8.4 KB per 4-kilobyte round trip. The first
comparison run missed it: the peer holds the public `TcpClientConnection` or
`TcpServerConnection`, and the wrappers did not forward `IOwnedFrameSink`.
Commit `20b14e4` fixed the wrappers and the drop appeared.
