using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using NsdRpc.Generators;

namespace NsdRpc.Generators.Tests;

public sealed class RpcServiceGeneratorTests
{
    [Fact]
    public void GeneratesClientForPlainCall()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using NsdRpc;
            using NsdRpc.Wire;

            [RpcService]
            public interface IEchoService
            {
                [RpcMethod(100)]
                Task<RpcResult<Handshake>> EchoAsync(Handshake request, CancellationToken cancellationToken = default);
            }
            """;

        (_, GeneratorDriverRunResult result, ImmutableArray<Diagnostic> diagnostics) = GeneratorRunner.Run(source);

        Assert.Empty(diagnostics);
        string text = GeneratedSource(result, "EchoServiceClient.g.cs");
        Assert.Contains("public sealed class EchoServiceClient : global::IEchoService", text);
        Assert.Contains("public EchoServiceClient(global::NsdRpc.RpcClient client)", text);
        Assert.Contains(
            "return _client.InvokeAsync<global::NsdRpc.Wire.Handshake, global::NsdRpc.Wire.Handshake>(100UL, request, null, cancellationToken);",
            text);
    }

    [Fact]
    public void GeneratesStreamSessionAndSinkOverloads()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using NsdRpc;
            using NsdRpc.Wire;

            [RpcService]
            public interface IEchoService
            {
                [RpcMethod(101)]
                RpcStreamSession<Handshake, Handshake> StreamAsync(Handshake request, TimeSpan? timeout = null, CancellationToken cancellationToken = default);

                [RpcMethod(101)]
                Task<RpcResult<Handshake>> StreamAsync(Handshake request, IChunkSink<Handshake> sink, CancellationToken cancellationToken = default);
            }
            """;

        (_, GeneratorDriverRunResult result, ImmutableArray<Diagnostic> diagnostics) = GeneratorRunner.Run(source);

        Assert.Empty(diagnostics);
        string text = GeneratedSource(result, "EchoServiceClient.g.cs");
        Assert.Contains(
            "public global::NsdRpc.RpcStreamSession<global::NsdRpc.Wire.Handshake, global::NsdRpc.Wire.Handshake> StreamAsync(global::NsdRpc.Wire.Handshake request, global::System.TimeSpan? timeout = default, global::System.Threading.CancellationToken cancellationToken = default)",
            text);
        Assert.Contains(
            "return _client.InvokeStreamAsync<global::NsdRpc.Wire.Handshake, global::NsdRpc.Wire.Handshake, global::NsdRpc.Wire.Handshake>(101UL, request, timeout, cancellationToken);",
            text);
        Assert.Contains(
            "return _client.InvokeStreamAsync<global::NsdRpc.Wire.Handshake, global::NsdRpc.Wire.Handshake, global::NsdRpc.Wire.Handshake>(101UL, request, sink, null, cancellationToken);",
            text);
    }

    [Fact]
    public void EmitsTheServiceAttribute()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using NsdRpc;
            using NsdRpc.Wire;

            [RpcService]
            public interface IEchoService
            {
                [RpcMethod(100)]
                Task<RpcResult<Handshake>> EchoAsync(Handshake request, CancellationToken cancellationToken = default);
            }
            """;

        (_, GeneratorDriverRunResult result, _) = GeneratorRunner.Run(source);

        Assert.Contains(
            result.Results[0].GeneratedSources,
            s => s.HintName == "NsdRpc.RpcServiceAttribute.g.cs");
    }

    [Fact]
    public void GeneratesHandlerBase()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using NsdRpc;
            using NsdRpc.Wire;

            [RpcService]
            public interface IEchoService
            {
                [RpcMethod(100)]
                Task<RpcResult<Handshake>> EchoAsync(Handshake request, CancellationToken cancellationToken = default);
            }
            """;

        (_, GeneratorDriverRunResult result, ImmutableArray<Diagnostic> diagnostics) = GeneratorRunner.Run(source);

        Assert.Empty(diagnostics);
        string text = GeneratedSource(result, "EchoAsyncHandler.g.cs");
        Assert.Contains("[global::NsdRpc.RpcMethod(100UL)]", text);
        Assert.Contains(
            "abstract class EchoAsyncHandler : global::NsdRpc.IRpcHandler<global::NsdRpc.Wire.Handshake, global::NsdRpc.Wire.Handshake>",
            text);
        Assert.Contains(
            "protected abstract global::System.Threading.Tasks.ValueTask<global::NsdRpc.RpcResult<global::NsdRpc.Wire.Handshake>> EchoAsync(",
            text);
    }

    [Fact]
    public void GeneratedHandlerCompilesAndRegisters()
    {
        const string source = """
            using System;
            using System.Reflection;
            using System.Threading;
            using System.Threading.Tasks;
            using NsdRpc;
            using NsdRpc.Wire;

            [RpcService]
            public interface IEchoService
            {
                [RpcMethod(100)]
                Task<RpcResult<Handshake>> EchoAsync(Handshake request, CancellationToken cancellationToken = default);
            }

            public sealed class TestHandler : EchoAsyncHandler
            {
                protected override ValueTask<RpcResult<Handshake>> EchoAsync(Handshake request, RpcContext context, CancellationToken cancellationToken)
                {
                    return new ValueTask<RpcResult<Handshake>>(RpcResult<Handshake>.Success(new Handshake(request.ProtocolVersion + 1)));
                }
            }

            public static class Driver
            {
                public static int Run()
                {
                    var registry = new RpcRegistry();
                    registry.Add(new TestHandler());
                    registry.Freeze();
                    RpcMethodAttribute? attribute = typeof(TestHandler).GetCustomAttribute<RpcMethodAttribute>(inherit: true);
                    if (attribute == null || attribute.Method != 100UL)
                    {
                        throw new Exception("the inherited method attribute is missing");
                    }

                    return 1;
                }
            }
            """;

        (Compilation compilation, _, ImmutableArray<Diagnostic> diagnostics) = GeneratorRunner.Run(source);

        Assert.Empty(diagnostics);
        Assert.Equal(1, RunDriver(compilation));
    }

    [Theory]
    [InlineData("RSG1001", """
        using NsdRpc;
        [RpcService]
        public class NotAnInterface
        {
        }
        """)]
    [InlineData("RSG1002", """
        using System.Threading.Tasks;
        using NsdRpc;
        using NsdRpc.Wire;
        [RpcService]
        public interface IEchoService
        {
            Task<RpcResult<Handshake>> MissingAsync(Handshake request);
        }
        """)]
    [InlineData("RSG1004", """
        using System.Threading.Tasks;
        using NsdRpc;
        using NsdRpc.Wire;
        [RpcService]
        public interface IEchoService
        {
            [RpcMethod(100)]
            Task<RpcResult<Handshake>> NoRequestAsync();
        }
        """)]
    [InlineData("RSG1005", """
        using System.Threading.Tasks;
        using NsdRpc;
        using NsdRpc.Wire;
        [RpcService]
        public interface IEchoService
        {
            [RpcMethod(100)]
            Task<RpcResult<Handshake>> BadAsync(object request);
        }
        """)]
    [InlineData("RSG1006", """
        using System.Threading.Tasks;
        using NsdRpc;
        using NsdRpc.Wire;
        [RpcService]
        public interface IEchoService
        {
            [RpcMethod(100)]
            Task<Handshake> BadAsync(Handshake request);
        }
        """)]
    [InlineData("RSG1007", """
        using System.Threading.Tasks;
        using NsdRpc;
        using NsdRpc.Wire;
        [RpcService]
        public interface IEchoService
        {
            [RpcMethod(100)]
            Task<RpcResult<string>> BadAsync(Handshake request);
        }
        """)]
    [InlineData("RSG1008", """
        using System.Threading.Tasks;
        using NsdRpc;
        using NsdRpc.Wire;
        [RpcService]
        public interface IEchoService
        {
            [RpcMethod(100)]
            Task<RpcResult<Handshake>> FirstAsync(Handshake request);
            [RpcMethod(100)]
            Task<RpcResult<Handshake>> SecondAsync(Handshake request);
        }
        """)]
    public void ReportsDiagnostic(string id, string source)
    {
        (_, _, ImmutableArray<Diagnostic> diagnostics) = GeneratorRunner.Run(source);

        Assert.Contains(diagnostics, d => d.Id == id);
    }

    [Fact]
    public void GeneratedClientCompilesAndRuns()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using NsdRpc;
            using NsdRpc.Wire;

            [RpcService]
            public interface IEchoService
            {
                [RpcMethod(100)]
                Task<RpcResult<Handshake>> EchoAsync(Handshake request, CancellationToken cancellationToken = default);

                [RpcMethod(101)]
                RpcStreamSession<Handshake, Handshake> StreamAsync(Handshake request, CancellationToken cancellationToken = default);
            }

            public static class Driver
            {
                public static int Run()
                {
                    var connection = new FakeConnection();
                    var client = new RpcClient(connection, new RpcRegistry());
                    var service = new EchoServiceClient(client);

                    _ = service.EchoAsync(new Handshake(7));
                    if (connection.Frames.Count != 1)
                    {
                        throw new Exception("the plain call must send one frame");
                    }

                    Envelope env = Envelope.Deserialize((ReadOnlySpan<byte>)connection.Frames[0].AsSpan(4));
                    if (env.Kind != (byte)RpcMessageKind.Request || env.Method != 100UL || env.Id != 1UL)
                    {
                        throw new Exception("the plain call frame is wrong");
                    }

                    Handshake payload = Handshake.Deserialize((ReadOnlySpan<byte>)env.Payload);
                    if (payload.ProtocolVersion != 7)
                    {
                        throw new Exception("the plain call payload is wrong");
                    }

                    RpcStreamSession<Handshake, Handshake> session = service.StreamAsync(new Handshake(2));
                    if (session == null)
                    {
                        throw new Exception("the stream call must return a session");
                    }

                    if (connection.Frames.Count != 2)
                    {
                        throw new Exception("the stream call must send one frame");
                    }

                    Envelope streamEnv = Envelope.Deserialize((ReadOnlySpan<byte>)connection.Frames[1].AsSpan(4));
                    if (streamEnv.Method != 101UL)
                    {
                        throw new Exception("the stream call frame is wrong");
                    }

                    return 0;
                }
            }

            public sealed class FakeConnection : IRpcConnection
            {
                public List<byte[]> Frames { get; } = new List<byte[]>();
                public bool IsConnected => true;
                public event Action Disconnected { add { } remove { } }
                public event RpcFrameHandler FrameReceived { add { } remove { } }
                public bool TrySend(ReadOnlySpan<byte> frame)
                {
                    Frames.Add(frame.ToArray());
                    return true;
                }
            }
            """;

        (Compilation compilation, _, ImmutableArray<Diagnostic> diagnostics) = GeneratorRunner.Run(source);

        Assert.Empty(diagnostics);
        Assert.Equal(0, RunDriver(compilation));
    }

    private static int RunDriver(Compilation compilation)
    {
        using var stream = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));

        Assembly assembly = Assembly.Load(stream.ToArray());
        Type driver = assembly.GetType("Driver")!;
        MethodInfo run = driver.GetMethod("Run")!;
        try
        {
            return (int)run.Invoke(null, null)!;
        }
        catch (TargetInvocationException exception)
        {
            Assert.Fail(exception.InnerException!.ToString());
            return 0;
        }
    }

    private static string GeneratedSource(GeneratorDriverRunResult result, string hintName)
    {
        GeneratedSourceResult source = result.Results[0].GeneratedSources.Single(s => s.HintName == hintName);
        return source.SourceText.ToString();
    }
}
