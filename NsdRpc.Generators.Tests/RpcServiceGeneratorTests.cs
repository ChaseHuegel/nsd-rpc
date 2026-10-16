using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using NsdRpc.Generators;

namespace NsdRpc.Generators.Tests;

public sealed class RpcServiceGeneratorTests
{
    [Fact]
    public void GeneratesClientForInvocationShapes()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using NsdRpc;
            using NsdRpc.Wire;

            [RpcService]
            public interface IChat
            {
                [RpcMethod(100)]
                Task<RpcResult<Handshake>> EchoAsync(Handshake request, CancellationToken cancellationToken = default);

                [RpcMethod(101)]
                RpcStreamSession<Handshake, Handshake> StreamAsync(Handshake request, TimeSpan? timeout = null, CancellationToken cancellationToken = default);

                [RpcMethod(101)]
                Task<RpcResult<Handshake>> StreamAsync(Handshake request, IChunkSink<Handshake> sink, CancellationToken cancellationToken = default);

                [RpcMethod(102)]
                void NotifyChat(Handshake message);

                [RpcMethod(103)]
                void Ping();
            }
            """;

        (_, GeneratorDriverRunResult result, ImmutableArray<Diagnostic> diagnostics) = GeneratorRunner.Run(source);

        Assert.Empty(diagnostics);
        string text = GeneratedSource(result, "ChatClient.g.cs");
        Assert.Contains("sealed class ChatClient : global::IChat", text);
        Assert.Contains(
            "return _caller.InvokeAsync<global::NsdRpc.Wire.Handshake, global::NsdRpc.Wire.Handshake>(100UL, request, null, cancellationToken);",
            text);
        Assert.Contains(
            "public global::NsdRpc.RpcStreamSession<global::NsdRpc.Wire.Handshake, global::NsdRpc.Wire.Handshake> StreamAsync(",
            text);
        Assert.Contains(
            "return _caller.InvokeStreamAsync<global::NsdRpc.Wire.Handshake, global::NsdRpc.Wire.Handshake, global::NsdRpc.Wire.Handshake>(101UL, request, timeout, cancellationToken);",
            text);
        Assert.Contains(
            "return _caller.InvokeStreamAsync<global::NsdRpc.Wire.Handshake, global::NsdRpc.Wire.Handshake, global::NsdRpc.Wire.Handshake>(101UL, request, sink, null, cancellationToken);",
            text);
        Assert.Contains("public void NotifyChat(global::NsdRpc.Wire.Handshake message)", text);
        Assert.Contains("_caller.Notify<global::NsdRpc.Wire.Handshake>(102UL, message);", text);
        Assert.Contains("public void Ping()", text);
        Assert.Contains("_caller.Notify(103UL);", text);
        Assert.DoesNotContain(result.Results[0].GeneratedSources, s => s.HintName == "ChatHandler.g.cs");
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
            public interface IChat
            {
                [RpcMethod(100)]
                ValueTask<RpcResult<Handshake>> EchoAsync(Handshake request, RpcContext context, CancellationToken cancellationToken);

                [RpcMethod(101)]
                void OnNotifyChat(Envelope message, RpcContext context, CancellationToken cancellationToken);
            }
            """;

        (_, GeneratorDriverRunResult result, ImmutableArray<Diagnostic> diagnostics) = GeneratorRunner.Run(source);

        Assert.Empty(diagnostics);
        string text = GeneratedSource(result, "ChatHandler.g.cs");
        Assert.Contains(
            "abstract class ChatHandler : global::IChat, global::NsdRpc.IRpcServiceRegistrar, global::NsdRpc.IRpcHandler<global::NsdRpc.Wire.Handshake, global::NsdRpc.Wire.Handshake>, global::NsdRpc.IRpcHandler<global::NsdRpc.Wire.Envelope, global::NsdRpc.RpcUnit>",
            text);
        Assert.Contains(
            "registry.Add<global::NsdRpc.Wire.Handshake, global::NsdRpc.Wire.Handshake>(this, 100UL, global::NsdRpc.RpcDirection.Both);",
            text);
        Assert.Contains(
            "registry.Add<global::NsdRpc.Wire.Envelope, global::NsdRpc.RpcUnit>(this, 101UL, global::NsdRpc.RpcDirection.Both);",
            text);
        Assert.Contains("public virtual global::System.Threading.Tasks.ValueTask<global::NsdRpc.RpcResult<global::NsdRpc.Wire.Handshake>> EchoAsync(", text);
        Assert.Contains("public virtual void OnNotifyChat(", text);
        Assert.Contains("if (context.RequestId != 0UL)", text);
        Assert.Contains(result.Results[0].GeneratedSources, s => s.HintName == "NsdRpc.RpcUnit.g.cs");
        Assert.DoesNotContain(result.Results[0].GeneratedSources, s => s.HintName == "ChatClient.g.cs");
    }

    [Fact]
    public void GeneratesDuplexPair()
    {
        const string source = """
            using System.Threading;
            using NsdRpc;
            using NsdRpc.Wire;

            [RpcService]
            public interface IChat
            {
                [RpcMethod(101)]
                void NotifyChat(Handshake message);

                [RpcMethod(101)]
                void OnNotifyChat(Handshake message, RpcContext context, CancellationToken cancellationToken);
            }
            """;

        (_, GeneratorDriverRunResult result, ImmutableArray<Diagnostic> diagnostics) = GeneratorRunner.Run(source);

        Assert.Empty(diagnostics);
        string client = GeneratedSource(result, "ChatClient.g.cs");
        string handler = GeneratedSource(result, "ChatHandler.g.cs");
        Assert.DoesNotContain(": global::IChat", client);
        Assert.DoesNotContain(": global::IChat", handler);
        Assert.Contains("public void NotifyChat(", client);
        Assert.Contains("public virtual void OnNotifyChat(", handler);
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
        public interface IChat
        {
            Task<RpcResult<Handshake>> MissingAsync(Handshake request);
        }
        """)]
    [InlineData("RSG1004", """
        using System.Threading;
        using System.Threading.Tasks;
        using NsdRpc;
        using NsdRpc.Wire;
        [RpcService]
        public interface IChat
        {
            [RpcMethod(100)]
            ValueTask<RpcResult<Handshake>> EchoAsync(RpcContext context, CancellationToken cancellationToken);
        }
        """)]
    [InlineData("RSG1005", """
        using System.Threading;
        using System.Threading.Tasks;
        using NsdRpc;
        using NsdRpc.Wire;
        [RpcService]
        public interface IChat
        {
            [RpcMethod(100)]
            ValueTask<RpcResult<Handshake>> EchoAsync(object request, RpcContext context, CancellationToken cancellationToken);
        }
        """)]
    [InlineData("RSG1006", """
        using System.Threading;
        using System.Threading.Tasks;
        using NsdRpc;
        using NsdRpc.Wire;
        [RpcService]
        public interface IChat
        {
            [RpcMethod(100)]
            Task<RpcResult<Handshake>> EchoAsync(Handshake request, RpcContext context, CancellationToken cancellationToken);
        }
        """)]
    [InlineData("RSG1007", """
        using System.Threading.Tasks;
        using NsdRpc;
        using NsdRpc.Wire;
        [RpcService]
        public interface IChat
        {
            [RpcMethod(100)]
            Task<RpcResult<string>> EchoAsync(Handshake request);
        }
        """)]
    [InlineData("RSG1008", """
        using System.Threading.Tasks;
        using NsdRpc;
        using NsdRpc.Wire;
        [RpcService]
        public interface IChat
        {
            [RpcMethod(100)]
            Task<RpcResult<Handshake>> FirstAsync(Handshake request);
            [RpcMethod(100)]
            Task<RpcResult<Handshake>> SecondAsync(Handshake request);
        }
        """)]
    [InlineData("RSG1010", """
        using System.Threading;
        using System.Threading.Tasks;
        using NsdRpc;
        using NsdRpc.Wire;
        [RpcService]
        public interface IChat
        {
            [RpcMethod(100)]
            ValueTask<RpcResult<Handshake>> FirstAsync(Handshake request, RpcContext context, CancellationToken cancellationToken);
            [RpcMethod(101)]
            ValueTask<RpcResult<Handshake>> SecondAsync(Handshake request, RpcContext context, CancellationToken cancellationToken);
        }
        """)]
    [InlineData("RSG1011", """
        using System.Threading;
        using System.Threading.Tasks;
        using NsdRpc;
        using NsdRpc.Wire;
        [RpcService]
        public interface IChat
        {
            [RpcMethod(100)]
            RpcStreamSession<Handshake, Handshake> StreamAsync(Handshake request, CancellationToken cancellationToken = default);
            [RpcMethod(100, Streams = typeof(Envelope))]
            ValueTask<RpcResult<Handshake>> StreamAsync(Handshake request, RpcContext context, CancellationToken cancellationToken);
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
            public interface IChat
            {
                [RpcMethod(100)]
                Task<RpcResult<Handshake>> EchoAsync(Handshake request, CancellationToken cancellationToken = default);

                [RpcMethod(101)]
                void Ping();
            }

            public static class Driver
            {
                public static int Run()
                {
                    var connection = new FakeConnection();
                    var client = new RpcClient(connection, new RpcRegistry());
                    var chat = new ChatClient(client);

                    _ = chat.EchoAsync(new Handshake(7));
                    chat.Ping();

                    if (connection.Frames.Count != 2)
                    {
                        throw new Exception("two frames expected");
                    }

                    Envelope first = Envelope.Deserialize((ReadOnlySpan<byte>)connection.Frames[0].AsSpan(4));
                    if (first.Kind != (byte)RpcMessageKind.Request || first.Method != 100UL || first.Id != 1UL)
                    {
                        throw new Exception("first frame is wrong");
                    }

                    Envelope second = Envelope.Deserialize((ReadOnlySpan<byte>)connection.Frames[1].AsSpan(4));
                    if (second.Kind != (byte)RpcMessageKind.Notification || second.Method != 101UL)
                    {
                        throw new Exception("second frame is wrong");
                    }

                    return 5;
                }
            }

            public sealed class FakeConnection : IRpcConnection
            {
                public List<byte[]> Frames { get; } = new List<byte[]>();
                public bool IsConnected => true;
                public void Close() { }
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
        Assert.Equal(5, RunDriver(compilation));
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
            public interface IChat
            {
                [RpcMethod(100)]
                ValueTask<RpcResult<Handshake>> EchoAsync(Handshake request, RpcContext context, CancellationToken cancellationToken);

                [RpcMethod(101)]
                void OnNotifyChat(Envelope message, RpcContext context, CancellationToken cancellationToken);
            }

            public sealed class TestHandler : ChatHandler
            {
                public override ValueTask<RpcResult<Handshake>> EchoAsync(Handshake request, RpcContext context, CancellationToken cancellationToken)
                {
                    return new ValueTask<RpcResult<Handshake>>(RpcResult<Handshake>.Success(new Handshake(request.ProtocolVersion + 1)));
                }

                public override void OnNotifyChat(Envelope message, RpcContext context, CancellationToken cancellationToken)
                {
                }
            }

            public static class Driver
            {
                public static int Run()
                {
                    var registry = new RpcRegistry();
                    registry.AddService(new TestHandler());
                    registry.Freeze();

                    Type[] interfaces = typeof(TestHandler).GetInterfaces();
                    bool hasEcho = Array.Exists(interfaces, i => i.Name == "IRpcHandler`2");
                    if (!hasEcho)
                    {
                        throw new Exception("the handler must implement IRpcHandler");
                    }

                    return 6;
                }
            }
            """;

        (Compilation compilation, _, ImmutableArray<Diagnostic> diagnostics) = GeneratorRunner.Run(source);

        Assert.Empty(diagnostics);
        Assert.Equal(6, RunDriver(compilation));
    }

    [Fact]
    public void GeneratedDuplexCompilesAndRuns()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using NsdRpc;
            using NsdRpc.Wire;

            [RpcService]
            public interface IChat
            {
                [RpcMethod(101)]
                void NotifyChat(Handshake message);

                [RpcMethod(101)]
                void OnNotifyChat(Handshake message, RpcContext context, CancellationToken cancellationToken);
            }

            public sealed class TestHandler : ChatHandler
            {
                public override void OnNotifyChat(Handshake message, RpcContext context, CancellationToken cancellationToken)
                {
                }
            }

            public static class Driver
            {
                public static int Run()
                {
                    var connection = new FakeConnection();
                    var chat = new ChatClient(new RpcClient(connection, new RpcRegistry()));
                    chat.NotifyChat(new Handshake(7));

                    if (connection.Frames.Count != 1)
                    {
                        throw new Exception("one notification expected");
                    }

                    Envelope frame = Envelope.Deserialize((ReadOnlySpan<byte>)connection.Frames[0].AsSpan(4));
                    if (frame.Kind != (byte)RpcMessageKind.Notification || frame.Method != 101UL || frame.Id != 0UL)
                    {
                        throw new Exception("notification frame is wrong");
                    }

                    var registry = new RpcRegistry();
                    registry.AddService(new TestHandler());
                    registry.Freeze();

                    return 7;
                }
            }

            public sealed class FakeConnection : IRpcConnection
            {
                public List<byte[]> Frames { get; } = new List<byte[]>();
                public bool IsConnected => true;
                public void Close() { }
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
        Assert.Equal(7, RunDriver(compilation));
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
