using System;

namespace TinyImage.Codecs.WebP.Lossy;

/// <summary>
/// Loop filter implementation for VP8 deblocking.
/// Translated from webp-rust loop_filter.rs
/// </summary>
internal static class LoopFilter
{
    private static int C(int val) => Math.Max(-128, Math.Min(127, val));
    private static int U2s(byte val) => val - 128;
    private static byte S2u(int val) => (byte)(C(val) + 128);
    private static byte Diff(byte val1, byte val2) => (byte)Math.Abs(val1 - val2);

    /// <summary>
    /// Common adjustment for vertical loop filter.
    /// </summary>
    private static int CommonAdjustVertical(bool useOuterTaps, byte[] pixels, int point, int stride)
    {
        int p1 = U2s(pixels[point - 2 * stride]);
        int p0 = U2s(pixels[point - stride]);
        int q0 = U2s(pixels[point]);
        int q1 = U2s(pixels[point + stride]);

        int outer = useOuterTaps ? C(p1 - q1) : 0;
        int a = C(outer + 3 * (q0 - p0));
        int b = C(a + 3) >> 3;
        a = C(a + 4) >> 3;

        pixels[point] = S2u(q0 - a);
        pixels[point - stride] = S2u(p0 + b);

        return a;
    }

    /// <summary>
    /// Common adjustment for horizontal loop filter.
    /// </summary>
    private static int CommonAdjustHorizontal(bool useOuterTaps, byte[] pixels, int offset)
    {
        int p1 = U2s(pixels[offset + 2]);
        int p0 = U2s(pixels[offset + 3]);
        int q0 = U2s(pixels[offset + 4]);
        int q1 = U2s(pixels[offset + 5]);

        int outer = useOuterTaps ? C(p1 - q1) : 0;
        int a = C(outer + 3 * (q0 - p0));
        int b = C(a + 3) >> 3;
        a = C(a + 4) >> 3;

        pixels[offset + 4] = S2u(q0 - a);
        pixels[offset + 3] = S2u(p0 + b);

        return a;
    }

    private static bool SimpleThresholdVertical(int filterLimit, byte[] pixels, int point, int stride)
    {
        return Diff(pixels[point - stride], pixels[point]) * 2
               + Diff(pixels[point - 2 * stride], pixels[point + stride]) / 2
               <= filterLimit;
    }

    private static bool SimpleThresholdHorizontal(int filterLimit, byte[] pixels, int offset)
    {
        return Diff(pixels[offset + 3], pixels[offset + 4]) * 2
               + Diff(pixels[offset + 2], pixels[offset + 5]) / 2
               <= filterLimit;
    }

    private static bool ShouldFilterVertical(byte interiorLimit, byte edgeLimit, byte[] pixels, int point, int stride)
    {
        return SimpleThresholdVertical(edgeLimit, pixels, point, stride)
               && Diff(pixels[point - 4 * stride], pixels[point - 3 * stride]) <= interiorLimit
               && Diff(pixels[point - 3 * stride], pixels[point - 2 * stride]) <= interiorLimit
               && Diff(pixels[point - 2 * stride], pixels[point - stride]) <= interiorLimit
               && Diff(pixels[point + 3 * stride], pixels[point + 2 * stride]) <= interiorLimit
               && Diff(pixels[point + 2 * stride], pixels[point + stride]) <= interiorLimit
               && Diff(pixels[point + stride], pixels[point]) <= interiorLimit;
    }

    private static bool ShouldFilterHorizontal(byte interiorLimit, byte edgeLimit, byte[] pixels, int offset)
    {
        return SimpleThresholdHorizontal(edgeLimit, pixels, offset)
               && Diff(pixels[offset], pixels[offset + 1]) <= interiorLimit
               && Diff(pixels[offset + 1], pixels[offset + 2]) <= interiorLimit
               && Diff(pixels[offset + 2], pixels[offset + 3]) <= interiorLimit
               && Diff(pixels[offset + 7], pixels[offset + 6]) <= interiorLimit
               && Diff(pixels[offset + 6], pixels[offset + 5]) <= interiorLimit
               && Diff(pixels[offset + 5], pixels[offset + 4]) <= interiorLimit;
    }

    private static bool HighEdgeVarianceVertical(byte threshold, byte[] pixels, int point, int stride)
    {
        return Diff(pixels[point - 2 * stride], pixels[point - stride]) > threshold
               || Diff(pixels[point + stride], pixels[point]) > threshold;
    }

    private static bool HighEdgeVarianceHorizontal(byte threshold, byte[] pixels, int offset)
    {
        return Diff(pixels[offset + 2], pixels[offset + 3]) > threshold
               || Diff(pixels[offset + 5], pixels[offset + 4]) > threshold;
    }

    /// <summary>
    /// Simple segment filter - vertical edge.
    /// </summary>
    public static void SimpleSegmentVertical(byte edgeLimit, byte[] pixels, int point, int stride)
    {
        if (SimpleThresholdVertical(edgeLimit, pixels, point, stride))
            CommonAdjustVertical(true, pixels, point, stride);
    }

    /// <summary>
    /// Simple segment filter - horizontal edge.
    /// </summary>
    public static void SimpleSegmentHorizontal(byte edgeLimit, byte[] pixels, int offset)
    {
        if (SimpleThresholdHorizontal(edgeLimit, pixels, offset))
            CommonAdjustHorizontal(true, pixels, offset);
    }

    /// <summary>
    /// Subblock filter - vertical edge.
    /// </summary>
    public static void SubblockFilterVertical(byte hevThreshold, byte interiorLimit, byte edgeLimit,
        byte[] pixels, int point, int stride)
    {
        if (ShouldFilterVertical(interiorLimit, edgeLimit, pixels, point, stride))
        {
            bool hv = HighEdgeVarianceVertical(hevThreshold, pixels, point, stride);
            int a = (CommonAdjustVertical(hv, pixels, point, stride) + 1) >> 1;

            if (!hv)
            {
                pixels[point + stride] = S2u(U2s(pixels[point + stride]) - a);
                pixels[point - 2 * stride] = S2u(U2s(pixels[point - 2 * stride]) + a);
            }
        }
    }

    /// <summary>
    /// Subblock filter - horizontal edge.
    /// </summary>
    public static void SubblockFilterHorizontal(byte hevThreshold, byte interiorLimit, byte edgeLimit,
        byte[] pixels, int offset)
    {
        if (ShouldFilterHorizontal(interiorLimit, edgeLimit, pixels, offset))
        {
            bool hv = HighEdgeVarianceHorizontal(hevThreshold, pixels, offset);
            int a = (CommonAdjustHorizontal(hv, pixels, offset) + 1) >> 1;

            if (!hv)
            {
                pixels[offset + 5] = S2u(U2s(pixels[offset + 5]) - a);
                pixels[offset + 2] = S2u(U2s(pixels[offset + 2]) + a);
            }
        }
    }

    /// <summary>
    /// Macroblock filter - vertical edge.
    /// </summary>
    public static void MacroblockFilterVertical(byte hevThreshold, byte interiorLimit, byte edgeLimit,
        byte[] pixels, int point, int stride)
    {
        if (ShouldFilterVertical(interiorLimit, edgeLimit, pixels, point, stride))
        {
            if (!HighEdgeVarianceVertical(hevThreshold, pixels, point, stride))
            {
                int p2 = U2s(pixels[point - 3 * stride]);
                int p1 = U2s(pixels[point - 2 * stride]);
                int p0 = U2s(pixels[point - stride]);
                int q0 = U2s(pixels[point]);
                int q1 = U2s(pixels[point + stride]);
                int q2 = U2s(pixels[point + 2 * stride]);

                int w = C(C(p1 - q1) + 3 * (q0 - p0));

                int a = C((27 * w + 63) >> 7);
                pixels[point] = S2u(q0 - a);
                pixels[point - stride] = S2u(p0 + a);

                a = C((18 * w + 63) >> 7);
                pixels[point + stride] = S2u(q1 - a);
                pixels[point - 2 * stride] = S2u(p1 + a);

                a = C((9 * w + 63) >> 7);
                pixels[point + 2 * stride] = S2u(q2 - a);
                pixels[point - 3 * stride] = S2u(p2 + a);
            }
            else
            {
                CommonAdjustVertical(true, pixels, point, stride);
            }
        }
    }

    /// <summary>
    /// Macroblock filter - horizontal edge.
    /// </summary>
    public static void MacroblockFilterHorizontal(byte hevThreshold, byte interiorLimit, byte edgeLimit,
        byte[] pixels, int offset)
    {
        if (ShouldFilterHorizontal(interiorLimit, edgeLimit, pixels, offset))
        {
            if (!HighEdgeVarianceHorizontal(hevThreshold, pixels, offset))
            {
                int p2 = U2s(pixels[offset + 1]);
                int p1 = U2s(pixels[offset + 2]);
                int p0 = U2s(pixels[offset + 3]);
                int q0 = U2s(pixels[offset + 4]);
                int q1 = U2s(pixels[offset + 5]);
                int q2 = U2s(pixels[offset + 6]);

                int w = C(C(p1 - q1) + 3 * (q0 - p0));

                int a = C((27 * w + 63) >> 7);
                pixels[offset + 4] = S2u(q0 - a);
                pixels[offset + 3] = S2u(p0 + a);

                a = C((18 * w + 63) >> 7);
                pixels[offset + 5] = S2u(q1 - a);
                pixels[offset + 2] = S2u(p1 + a);

                a = C((9 * w + 63) >> 7);
                pixels[offset + 6] = S2u(q2 - a);
                pixels[offset + 1] = S2u(p2 + a);
            }
            else
            {
                CommonAdjustHorizontal(true, pixels, offset);
            }
        }
    }
}
