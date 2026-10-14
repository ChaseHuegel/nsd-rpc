# Improvement: Typed service clients via the RpcService source generator

- Type: improvement
- Status: done
- Workflow: ../specs/issues.md

## Problem

Every outbound call spelled out two type arguments, an explicit method id,
and an `in` modifier. A typed client should read like an ordinary instance
method: `Task<RpcResult<ListResponse>> ListAsync(ListRequest)` with the
method id and generics implied. The pattern is mechanical, so a source
generator should produce it. See [../../specs/rpc-api.md](../../specs/rpc-api.md).

## Acceptance criteria

- [x] `NsdRpc.Generators` targets `netstandard2.0`, references `Microsoft.CodeAnalysis.CSharp`, and joins the solution.
- [x] `RpcServiceAttribute` is declared once in the generator project and emitted into consumer compilations. The analyzer csproj defines a symbol so the same source file compiles both ways without a duplicate type.
- [x] The generator emits a `{Name}Client` class for every interface marked `[RpcService]`. The leading `I` is stripped from the name.
- [x] Three method shapes are supported: a plain call, a stream returning `RpcStreamSession<TChunk, TResponse>`, and a stream taking an `IChunkSink<TChunk>`.
- [x] An optional `TimeSpan?` timeout parameter and a trailing `CancellationToken` are forwarded to the underlying call.
- [x] The generated class is `public` or `internal` to match the interface.
- [x] Diagnostics RSG1001 through RSG1008 fire for malformed declarations: attribute on a non-interface, a method without `[RpcMethod]`, a non-constant method id, a wrong request parameter count, non-struct payload types, an unsupported return type, and a duplicate method id.
- [x] `RpcMethodAttribute` allows `Method` targets in the core library.
- [x] `NsdRpc.Generators.Tests` is a dedicated xunit project. GeneratorDriver tests cover every diagnostic and every shape. A compile-and-run test proves the generated client forwards to the real API and a frame carries the right method id and payload.
- [x] The docs pass: a "Typed service clients" section in [rpc-api](../../specs/rpc-api.md), rows in [architecture](../../architecture.md), and a note in [development](../../development.md). The publish workflow packs the generator.
