namespace TinyImage.Codecs.WebP.Lossy;

/// <summary>
/// DCT (Discrete Cosine Transform) implementation for VP8.
/// Translated from webp-rust transform.rs
/// </summary>
internal static class DctTransform
{
    // 16 bit fixed point version of cos(PI/8) * sqrt(2) - 1
    private const long Const1 = 20091;
    // 16 bit fixed point version of sin(PI/8) * sqrt(2)
    private const long Const2 = 35468;

    /// <summary>
    /// Inverse DCT 4x4 transform used in decoding.
    /// </summary>
    public static void Idct4x4(int[] block)
    {
        // Column transform
        for (int i = 0; i < 4; i++)
        {
            long a1 = block[i] + block[8 + i];
            long b1 = block[i] - block[8 + i];

            long t1 = (block[4 + i] * Const2) >> 16;
            long t2 = block[12 + i] + ((block[12 + i] * Const1) >> 16);
            long c1 = t1 - t2;

            t1 = block[4 + i] + ((block[4 + i] * Const1) >> 16);
            t2 = (block[12 + i] * Const2) >> 16;
            long d1 = t1 + t2;

            block[i] = (int)(a1 + d1);
            block[4 + i] = (int)(b1 + c1);
            block[3 * 4 + i] = (int)(a1 - d1);
            block[2 * 4 + i] = (int)(b1 - c1);
        }

        // Row transform
        for (int i = 0; i < 4; i++)
        {
            long a1 = block[4 * i] + block[4 * i + 2];
            long b1 = block[4 * i] - block[4 * i + 2];

            long t1 = (block[4 * i + 1] * Const2) >> 16;
            long t2 = block[4 * i + 3] + ((block[4 * i + 3] * Const1) >> 16);
            long c1 = t1 - t2;

            t1 = block[4 * i + 1] + ((block[4 * i + 1] * Const1) >> 16);
            t2 = (block[4 * i + 3] * Const2) >> 16;
            long d1 = t1 + t2;

            block[4 * i] = (int)((a1 + d1 + 4) >> 3);
            block[4 * i + 3] = (int)((a1 - d1 + 4) >> 3);
            block[4 * i + 1] = (int)((b1 + c1 + 4) >> 3);
            block[4 * i + 2] = (int)((b1 - c1 + 4) >> 3);
        }
    }

    /// <summary>
    /// Forward DCT 4x4 transform used in encoding.
    /// </summary>
    public static void Dct4x4(int[] block)
    {
        // Vertical transform
        for (int i = 0; i < 4; i++)
        {
            long a = (block[i * 4] + block[i * 4 + 3]) * 8;
            long b = (block[i * 4 + 1] + block[i * 4 + 2]) * 8;
            long c = (block[i * 4 + 1] - block[i * 4 + 2]) * 8;
            long d = (block[i * 4] - block[i * 4 + 3]) * 8;

            block[i * 4] = (int)(a + b);
            block[i * 4 + 2] = (int)(a - b);
            block[i * 4 + 1] = (int)((c * 2217 + d * 5352 + 14500) >> 12);
            block[i * 4 + 3] = (int)((d * 2217 - c * 5352 + 7500) >> 12);
        }

        // Horizontal transform
        for (int i = 0; i < 4; i++)
        {
            long a = block[i] + block[i + 12];
            long b = block[i + 4] + block[i + 8];
            long c = block[i + 4] - block[i + 8];
            long d = block[i] - block[i + 12];

            block[i] = (int)((a + b + 7) >> 4);
            block[i + 8] = (int)((a - b + 7) >> 4);
            block[i + 4] = (int)(((c * 2217 + d * 5352 + 12000) >> 16) + (d != 0 ? 1 : 0));
            block[i + 12] = (int)((d * 2217 - c * 5352 + 51000) >> 16);
        }
    }
}
