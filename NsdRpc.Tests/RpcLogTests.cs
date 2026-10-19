using System.Buffers.Binary;
using System.Diagnostics;
using NsdRpc;
using NsdRpc.Wire;

namespace NsdRpc.Tests;

public sealed class RpcLogTests
{
    private sealed class CaptureListener : TraceListener
    {
        public string Text { get; private set; } = string.Empty;

        public override void Write(string? message)
        {
            Text += message;
        }

        public override void WriteLine(string? message)
        {
            Text += message;
        }
    }

    [Fact]
    public void DroppedFrameDoesNotAllocateWhenTracingIsOff()
    {
        var peer = new RpcPeer(new InMemoryConnection());
        var envelope = new Envelope(1, 9, 1UL, 1UL, null, null);
        byte[] body = envelope.Serialize();
        byte[] frame = new byte[4 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, body.Length);
        body.CopyTo(frame, 4);

        // Warm up.
        peer.HandleFrame(frame);

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 100; index++)
        {
            peer.HandleFrame(frame);
        }

        long allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(allocatedBefore, allocatedAfter);
    }

    [Fact]
    public void ASinkReceivesEventsWithoutATraceSourceListener()
    {
        var events = new List<string>();
        SourceLevels previous = RpcLog.Source.Switch.Level;
        RpcLog.Source.Switch.Level = SourceLevels.Off;
        try
        {
            RpcLog.Drop(
                (level, message, exception) => events.Add(level + ":" + message),
                "reason text");
            RpcLog.Error(
                (level, message, exception) => events.Add(level + ":" + message),
                "error text",
                new InvalidOperationException("boom"));
        }
        finally
        {
            RpcLog.Source.Switch.Level = previous;
        }

        Assert.Contains("Warning:Dropped frame: reason text", events);
        Assert.Contains("Error:error text", events);
    }

    [Fact]
    public void AThrowingSinkIsContained()
    {
        Action<TraceEventType, string, Exception?> sink =
            (level, message, exception) => throw new InvalidOperationException("sink failed");

        RpcLog.Drop(sink, "reason text");
        RpcLog.Error(sink, "error text", new InvalidOperationException("boom"));
    }
}
