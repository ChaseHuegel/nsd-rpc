using System.IO;

namespace NsdRpc.Tests;

internal static class TruncatedBufferHelper
{
    public delegate byte[] Serialize<T>(in T value);

    public delegate int Unpack<T>(ref T value, ReadOnlySpan<byte> data);

    // Scans every truncation of a valid buffer and asserts the reader contract:
    // it never reads out of bounds. A truncated field throws InvalidDataException.
    // Anything else must report a consumed count within the buffer.
    public static void AssertEveryTruncationIsSafe<T>(
        in T sample, Serialize<T> serialize, Unpack<T> unpack)
        where T : struct
    {
        byte[] full = serialize(in sample);
        Assert.True(full.Length > 0, "The sample must serialize to a nonempty buffer.");

        for (int length = 0; length < full.Length; length++)
        {
            var value = default(T);
            int consumed;
            try
            {
                consumed = unpack(ref value, full.AsSpan(0, length));
            }
            catch (InvalidDataException)
            {
                continue;
            }

            Assert.InRange(consumed, 0, length);
        }
    }
}
