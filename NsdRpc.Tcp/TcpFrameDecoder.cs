using System.Buffers.Binary;
using NsdRpc;

namespace NsdRpc.Tcp;

// Turns a byte stream into frames. A frame is a 4-byte little-endian length
// prefix and the payload bytes. The decoder rejects a zero length, a length
// above MaxFrameBytes, and an incomplete frame stays buffered.
internal sealed class TcpFrameDecoder
{
    private readonly int _maxFrameBytes;
    private byte[] _buffer;
    private int _count;

    public TcpFrameDecoder(int maxFrameBytes)
    {
        _maxFrameBytes = maxFrameBytes;
        _buffer = new byte[4096];
    }

    public int MaxFrameBytes => _maxFrameBytes;

    public bool TryPush(ReadOnlySpan<byte> data, RpcFrameHandler onFrame)
    {
        int needed = _count + data.Length;
        if (needed > _buffer.Length)
        {
            int newSize = _buffer.Length;
            while (newSize < needed)
            {
                newSize *= 2;
            }

            if (newSize > _maxFrameBytes + 4)
            {
                newSize = _maxFrameBytes + 4;
            }

            if (newSize < needed)
            {
                return false;
            }

            Array.Resize(ref _buffer, newSize);
        }

        data.CopyTo(_buffer.AsSpan(_count));
        _count = needed;

        int start = 0;
        while (_count - start >= 4)
        {
            int length = BinaryPrimitives.ReadInt32LittleEndian(_buffer.AsSpan(start, 4));
            if (length <= 0 || length > _maxFrameBytes)
            {
                return false;
            }

            if (_count - start < 4 + length)
            {
                break;
            }

            // Emit the whole frame: the length prefix and the payload.
            onFrame(_buffer.AsSpan(start, 4 + length));
            start += 4 + length;
        }

        if (start > 0)
        {
            _count -= start;
            Buffer.BlockCopy(_buffer, start, _buffer, 0, _count);
        }

        return true;
    }
}
