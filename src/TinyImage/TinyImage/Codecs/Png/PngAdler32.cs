using System.Collections.Generic;

namespace TinyImage.Codecs.Png;

/// <summary>
/// Adler-32 checksum for ZLIB data.
/// </summary>
internal static class PngAdler32
{
    private const int AdlerModulus = 65521;

    public static int Calculate(IEnumerable<byte> data, int length = -1)
    {
        var s1 = 1;
        var s2 = 0;
        var count = 0;

        foreach (var b in data)
        {
            if (length > 0 && count == length)
                break;

            s1 = (s1 + b) % AdlerModulus;
            s2 = (s1 + s2) % AdlerModulus;
            count++;
        }

        return (s2 << 16) + s1;
    }
}
