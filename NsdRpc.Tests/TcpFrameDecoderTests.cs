using System.Buffers.Binary;
using NsdRpc.Tcp;

namespace NsdRpc.Tests;

public sealed class TcpFrameDecoderTests
{
    private static byte[] Frame(params byte[] payload)
    {
        byte[] frame = new byte[4 + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, payload.Length);
        payload.CopyTo(frame, 4);
        return frame;
    }

    private static void AssertFrames(List<byte[]> frames, params byte[][] expected)
    {
        Assert.Equal(expected.Length, frames.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(Frame(expected[i]), frames[i]);
        }
    }

    [Fact]
    public void DecoderParsesOneFrame()
    {
        var decoder = new TcpFrameDecoder(1024);
        List<byte[]> frames = [];

        bool ok = decoder.TryPush(Frame([1, 2, 3]), span => frames.Add(span.ToArray()));

        Assert.True(ok);
        AssertFrames(frames, [1, 2, 3]);
    }

    [Fact]
    public void DecoderParsesMultipleFramesPerPush()
    {
        var decoder = new TcpFrameDecoder(1024);
        List<byte[]> frames = [];
        byte[] stream = [.. Frame([1]), .. Frame([2, 2]), .. Frame([3, 3, 3])];

        bool ok = decoder.TryPush(stream, span => frames.Add(span.ToArray()));

        Assert.True(ok);
        AssertFrames(frames, [1], [2, 2], [3, 3, 3]);
    }

    [Fact]
    public void DecoderBuffersPartialFrames()
    {
        var decoder = new TcpFrameDecoder(1024);
        List<byte[]> frames = [];
        byte[] stream = Frame([9, 8, 7, 6]);

        bool first = decoder.TryPush(stream.AsSpan(0, 2), span => frames.Add(span.ToArray()));
        bool second = decoder.TryPush(stream.AsSpan(2, 3), span => frames.Add(span.ToArray()));
        bool third = decoder.TryPush(stream.AsSpan(5), span => frames.Add(span.ToArray()));

        Assert.True(first);
        Assert.True(second);
        Assert.True(third);
        AssertFrames(frames, [9, 8, 7, 6]);
    }

    [Fact]
    public void DecoderSplitsFramesAtEveryByteBoundary()
    {
        var decoder = new TcpFrameDecoder(1024);
        List<byte[]> frames = [];
        byte[] stream = [.. Frame([4, 4]), .. Frame([5, 5, 5])];

        for (int i = 0; i < stream.Length; i++)
        {
            Assert.True(decoder.TryPush(stream.AsSpan(i, 1), span => frames.Add(span.ToArray())));
        }

        AssertFrames(frames, [4, 4], [5, 5, 5]);
    }

    [Fact]
    public void DecoderRejectsZeroLengthFrame()
    {
        var decoder = new TcpFrameDecoder(1024);

        bool ok = decoder.TryPush(Frame(), span => { });

        Assert.False(ok);
    }

    [Fact]
    public void DecoderRejectsOversizeFrame()
    {
        var decoder = new TcpFrameDecoder(12);

        bool ok = decoder.TryPush(Frame([1, 2, 3, 4, 5, 6, 7, 8, 9]), span => { });

        Assert.False(ok);
    }

    [Fact]
    public void DecoderAcceptsFrameAtTheCap()
    {
        var decoder = new TcpFrameDecoder(12);
        List<byte[]> frames = [];

        bool ok = decoder.TryPush(Frame([1, 2, 3, 4, 5, 6, 7, 8]), span => frames.Add(span.ToArray()));

        Assert.True(ok);
        AssertFrames(frames, [1, 2, 3, 4, 5, 6, 7, 8]);
    }

    [Fact]
    public void DecoderRejectsFrameOneByteAboveTheCap()
    {
        var decoder = new TcpFrameDecoder(11);

        bool ok = decoder.TryPush(Frame([1, 2, 3, 4, 5, 6, 7, 8]), span => { });

        Assert.False(ok);
    }

    [Fact]
    public void DecoderGrowsTheBufferForAFrameAboveFourKilobytes()
    {
        var decoder = new TcpFrameDecoder(16384);
        List<byte[]> frames = [];
        byte[] payload = new byte[5000];
        payload[0] = 7;
        payload[^1] = 9;
        byte[] stream = Frame(payload);

        bool first = decoder.TryPush(stream.AsSpan(0, 3), span => frames.Add(span.ToArray()));
        bool second = decoder.TryPush(stream.AsSpan(3), span => frames.Add(span.ToArray()));

        Assert.True(first);
        Assert.True(second);
        AssertFrames(frames, payload);
    }

    [Fact]
    public void DecoderAcceptsAMaximumFrameSplitBeforeTheNextFrame()
    {
        var decoder = new TcpFrameDecoder(4100);
        List<byte[]> frames = [];
        byte[] payload = new byte[4096];
        byte[] firstFrame = Frame(payload);
        byte[] stream = [.. firstFrame, .. Frame([1, 2, 3])];

        // The first part stops one byte short of the maximum frame.
        bool first = decoder.TryPush(
            stream.AsSpan(0, firstFrame.Length - 1), span => frames.Add(span.ToArray()));
        // The second part holds the missing byte and the whole next frame.
        bool second = decoder.TryPush(
            stream.AsSpan(firstFrame.Length - 1), span => frames.Add(span.ToArray()));

        Assert.True(first);
        Assert.True(second);
        AssertFrames(frames, payload, [1, 2, 3]);
    }

    [Fact]
    public void DecoderAcceptsAReadAboveTheCapThatHoldsAFrameAndTheNextStart()
    {
        var decoder = new TcpFrameDecoder(4100);
        List<byte[]> frames = [];
        byte[] payload = new byte[4096];
        byte[] firstFrame = Frame(payload);
        byte[] stream = [.. firstFrame, .. Frame([1, 2, 3])];
        int read = firstFrame.Length + 4;

        // The read holds the whole first frame and the next frame prefix. It
        // is above the frame cap.
        bool first = decoder.TryPush(stream.AsSpan(0, read), span => frames.Add(span.ToArray()));
        bool second = decoder.TryPush(stream.AsSpan(read), span => frames.Add(span.ToArray()));

        Assert.True(read > decoder.MaxFrameBytes);
        Assert.True(first);
        Assert.True(second);
        AssertFrames(frames, payload, [1, 2, 3]);
    }

    [Fact]
    public void DecoderFuzzesSplitPointsNearTheCap()
    {
        var decoder = new TcpFrameDecoder(4100);
        List<byte[]> frames = [];
        byte[] payload = new byte[4096];
        for (int index = 0; index < payload.Length; index++)
        {
            payload[index] = (byte)index;
        }

        byte[] stream = [.. Frame(payload), .. Frame([1, 2, 3])];
        var random = new Random(6789);
        int offset = 0;
        while (offset < stream.Length)
        {
            int chunk = Math.Min(random.Next(1, 512), stream.Length - offset);
            Assert.True(
                decoder.TryPush(stream.AsSpan(offset, chunk), span => frames.Add(span.ToArray())),
                "The decoder rejected valid data at offset " + offset);
            offset += chunk;
        }

        AssertFrames(frames, payload, [1, 2, 3]);
    }

    [Fact]
    public void DecoderFuzzNeverThrows()
    {
        var decoder = new TcpFrameDecoder(64);
        var random = new Random(12345);
        byte[] bytes = new byte[4096];
        random.NextBytes(bytes);
        List<byte[]> frames = [];

        int offset = 0;
        while (offset < bytes.Length)
        {
            int chunk = Math.Min(random.Next(1, 17), bytes.Length - offset);
            if (!decoder.TryPush(bytes.AsSpan(offset, chunk), span => frames.Add(span.ToArray())))
            {
                return;
            }

            offset += chunk;
        }

        foreach (byte[] frame in frames)
        {
            Assert.InRange(frame.Length, 1, 64);
        }
    }
}
