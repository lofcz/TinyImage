namespace TinyImage.Codecs.WebP.Lossy;

/// <summary>
/// Walsh-Hadamard Transform implementation for VP8.
/// Translated from webp-rust transform.rs
/// </summary>
internal static class WhtTransform
{
    /// <summary>
    /// Inverse Walsh-Hadamard 4x4 transform used in decoding.
    /// Section 14.3 of VP8 specification.
    /// </summary>
    public static void Iwht4x4(int[] block)
    {
        // Column transform
        for (int i = 0; i < 4; i++)
        {
            int a1 = block[i] + block[12 + i];
            int b1 = block[4 + i] + block[8 + i];
            int c1 = block[4 + i] - block[8 + i];
            int d1 = block[i] - block[12 + i];

            block[i] = a1 + b1;
            block[4 + i] = c1 + d1;
            block[8 + i] = a1 - b1;
            block[12 + i] = d1 - c1;
        }

        // Row transform
        for (int row = 0; row < 4; row++)
        {
            int offset = row * 4;
            int a1 = block[offset] + block[offset + 3];
            int b1 = block[offset + 1] + block[offset + 2];
            int c1 = block[offset + 1] - block[offset + 2];
            int d1 = block[offset] - block[offset + 3];

            int a2 = a1 + b1;
            int b2 = c1 + d1;
            int c2 = a1 - b1;
            int d2 = d1 - c1;

            block[offset] = (a2 + 3) >> 3;
            block[offset + 1] = (b2 + 3) >> 3;
            block[offset + 2] = (c2 + 3) >> 3;
            block[offset + 3] = (d2 + 3) >> 3;
        }
    }

    /// <summary>
    /// Forward Walsh-Hadamard 4x4 transform used in encoding.
    /// </summary>
    public static void Wht4x4(int[] block)
    {
        // Vertical transform
        for (int i = 0; i < 4; i++)
        {
            long a = block[i * 4] + block[i * 4 + 3];
            long b = block[i * 4 + 1] + block[i * 4 + 2];
            long c = block[i * 4 + 1] - block[i * 4 + 2];
            long d = block[i * 4] - block[i * 4 + 3];

            block[i * 4] = (int)(a + b);
            block[i * 4 + 1] = (int)(c + d);
            block[i * 4 + 2] = (int)(a - b);
            block[i * 4 + 3] = (int)(d - c);
        }

        // Horizontal transform
        for (int i = 0; i < 4; i++)
        {
            long a1 = block[i] + block[i + 12];
            long b1 = block[i + 4] + block[i + 8];
            long c1 = block[i + 4] - block[i + 8];
            long d1 = block[i] - block[i + 12];

            long a2 = a1 + b1;
            long b2 = c1 + d1;
            long c2 = a1 - b1;
            long d2 = d1 - c1;

            long a3 = (a2 + (a2 > 0 ? 1 : 0)) / 2;
            long b3 = (b2 + (b2 > 0 ? 1 : 0)) / 2;
            long c3 = (c2 + (c2 > 0 ? 1 : 0)) / 2;
            long d3 = (d2 + (d2 > 0 ? 1 : 0)) / 2;

            block[i] = (int)a3;
            block[i + 4] = (int)b3;
            block[i + 8] = (int)c3;
            block[i + 12] = (int)d3;
        }
    }
}
