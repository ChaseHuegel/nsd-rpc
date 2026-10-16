using System.Buffers.Binary;
using NsdRpc;

namespace NsdRpc.Tcp;

// Turns a byte stream into frames. A frame is a 4-byte little-endian length
// prefix and the payload bytes, so MaxFrameBytes bounds the frame including
// the prefix. The decoder rejects a zero length and a frame above
// MaxFrameBytes. An incomplete frame stays buffered.
//
// The buffer holds at most one frame. A read that spans a frame boundary is
// copied and processed in slices, so a large read never needs a large buffer.
internal sealed class TcpFrameDecoder
{
    public const int StartBufferBytes = 4096;

    private readonly int _maxFrameBytes;
    // The idle sweep shrinks the buffer while the receive loop may push. The
    // lock keeps the two off each other.
    private readonly object _gate = new();
    private byte[] _buffer;
    private int _count;

    public TcpFrameDecoder(int maxFrameBytes)
    {
        _maxFrameBytes = maxFrameBytes;
        _buffer = new byte[StartBufferBytes];
    }

    public int MaxFrameBytes => _maxFrameBytes;

    public int BufferBytes => _buffer.Length;

    public bool TryPush(ReadOnlySpan<byte> data, RpcFrameHandler onFrame)
    {
        lock (_gate)
        {
            return TryPushLocked(data, onFrame);
        }
    }

    // Shrinks the buffer to the start size. Returns false when the decoder
    // holds a partial frame or already sits at the start size.
    public bool TryShrink()
    {
        lock (_gate)
        {
            if (_count != 0)
            {
                return false;
            }

            if (_buffer.Length == StartBufferBytes)
            {
                return false;
            }

            _buffer = new byte[StartBufferBytes];
            return true;
        }
    }

    private bool TryPushLocked(ReadOnlySpan<byte> data, RpcFrameHandler onFrame)
    {
        if (data.Length == 0)
        {
            return TryEmitFrames(onFrame);
        }

        int offset = 0;
        while (offset < data.Length)
        {
            if (_count == _buffer.Length)
            {
                // Complete frames leave the buffer before it grows.
                if (!TryEmitFrames(onFrame))
                {
                    return false;
                }

                if (_count == _buffer.Length && !TryGrowBuffer())
                {
                    return false;
                }
            }

            int chunk = Math.Min(_buffer.Length - _count, data.Length - offset);
            data.Slice(offset, chunk).CopyTo(_buffer.AsSpan(_count));
            _count += chunk;
            offset += chunk;

            if (!TryEmitFrames(onFrame))
            {
                return false;
            }
        }

        return true;
    }

    // Emits every complete frame and compacts the remainder to the front.
    private bool TryEmitFrames(RpcFrameHandler onFrame)
    {
        int start = 0;
        while (_count - start >= 4)
        {
            int length = BinaryPrimitives.ReadInt32LittleEndian(_buffer.AsSpan(start, 4));
            if (length <= 0 || length > _maxFrameBytes - 4)
            {
                return false;
            }

            if (_count - start < 4 + length)
            {
                break;
            }

            onFrame(_buffer.AsSpan(start, 4 + length));
            start += 4 + length;
        }

        if (start > 0)
        {
            _count -= start;
            Buffer.BlockCopy(_buffer, start, _buffer, 0, _count);
        }

        Invariant.That(
            _count <= _maxFrameBytes,
            "The frame decoder retains more than one frame.");
        return true;
    }

    private bool TryGrowBuffer()
    {
        int newSize = _buffer.Length * 2;
        if (newSize > _maxFrameBytes)
        {
            newSize = _maxFrameBytes;
        }

        if (newSize <= _buffer.Length)
        {
            return false;
        }

        Array.Resize(ref _buffer, newSize);
        return true;
    }
}
