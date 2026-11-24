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

        public void Clear()
        {
            Text = string.Empty;
        }

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
        using RpcLogScope scope = RpcLogScope.WithLevel(SourceLevels.Off);
        var peer = new RpcPeer(new InMemoryConnection());

        // The short frame reaches RpcLog.Drop without a caller guard. An
        // allocation inside Drop with the switch off fails this test.
        long allocated = MeasureDroppedFrames(peer, new byte[2]);

        Assert.Equal(0L, allocated);
    }

    [Fact]
    public void UnknownKindFrameDoesNotBuildTheDropTextWhenTracingIsOff()
    {
        using RpcLogScope scope = RpcLogScope.WithLevel(SourceLevels.Off);
        var peer = new RpcPeer(new InMemoryConnection());

        long allocated = MeasureDroppedFrames(peer, CreateUnknownKindFrame());

        Assert.Equal(0L, allocated);
    }

    [Fact]
    public void DroppedFrameAllocatesWhenTracingIsOn()
    {
        var listener = new CaptureListener();
        using RpcLogScope scope =
            RpcLogScope.WithLevelAndListener(SourceLevels.Warning, listener);
        var peer = new RpcPeer(new InMemoryConnection());

        long allocated = MeasureDroppedFrames(peer, CreateUnknownKindFrame(), listener);

        Assert.True(allocated > 0L);
        Assert.Contains("unknown message kind 9", listener.Text);
    }

    [Fact]
    public void DropDeliversTheReasonWhenTheSwitchIsOn()
    {
        var listener = new CaptureListener();
        using RpcLogScope scope =
            RpcLogScope.WithLevelAndListener(SourceLevels.Warning, listener);

        RpcLog.Drop(null, "reason text");

        Assert.Contains("Dropped frame: reason text", listener.Text);
    }

    [Fact]
    public void ASinkReceivesEventsWithoutATraceSourceListener()
    {
        var events = new List<string>();
        using RpcLogScope scope = RpcLogScope.WithLevel(SourceLevels.Off);

        RpcLog.Drop(
            (level, message, exception) => events.Add(level + ":" + message),
            "reason text");
        RpcLog.Error(
            (level, message, exception) => events.Add(level + ":" + message),
            "error text",
            new InvalidOperationException("boom"));

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

    private static long MeasureDroppedFrames(
        RpcPeer peer, byte[] frame, CaptureListener? listener = null)
    {
        // Warm up, then clear the captured text so the assertion covers the
        // measured loop only.
        peer.HandleFrame(frame);
        listener?.Clear();

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 100; index++)
        {
            peer.HandleFrame(frame);
        }

        long allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
        return allocatedAfter - allocatedBefore;
    }

    private static byte[] CreateUnknownKindFrame()
    {
        var envelope = new Envelope(1, 9, 1UL, 1UL, null, null);
        byte[] body = envelope.Serialize();
        byte[] frame = new byte[4 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, body.Length);
        body.CopyTo(frame, 4);
        return frame;
    }
}
