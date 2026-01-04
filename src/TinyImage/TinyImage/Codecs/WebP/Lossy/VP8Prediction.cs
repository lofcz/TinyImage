using System;

namespace TinyImage.Codecs.WebP.Lossy;

/// <summary>
/// VP8 prediction modes.
/// </summary>
internal enum LumaMode { DC = 0, V = 1, H = 2, TM = 3, B = 4 }
internal enum ChromaMode { DC = 0, V = 1, H = 2, TM = 3 }
internal enum IntraMode { DC = 0, TM = 1, VE = 2, HE = 3, LD = 4, RD = 5, VR = 6, VL = 7, HD = 8, HU = 9 }

/// <summary>
/// VP8 intra prediction implementation.
/// Translated from webp-rust vp8_prediction.rs
/// </summary>
internal static class VP8Prediction
{
    public const int LumaBlockSize = (1 + 16 + 4) * (1 + 16);
    public const int LumaStride = 1 + 16 + 4;
    public const int ChromaBlockSize = (8 + 1) * (8 + 1);
    public const int ChromaStride = 8 + 1;

    public static byte[] CreateBorderLuma(int mbx, int mby, int mbw, byte[] top, byte[] left)
    {
        byte[] ws = new byte[LumaBlockSize];

        // Above row
        if (mby == 0)
        {
            for (int i = 1; i < LumaStride; i++)
                ws[i] = 127;
        }
        else
        {
            for (int i = 0; i < 16; i++)
                ws[1 + i] = top[mbx * 16 + i];

            if (mbx == mbw - 1)
            {
                for (int i = 16; i < LumaStride - 1; i++)
                    ws[1 + i] = top[mbx * 16 + 15];
            }
            else
            {
                for (int i = 0; i < 4; i++)
                    ws[17 + i] = top[mbx * 16 + 16 + i];
            }
        }

        // Copy above row to other rows for TR prediction
        for (int i = 17; i < LumaStride; i++)
        {
            ws[4 * LumaStride + i] = ws[i];
            ws[8 * LumaStride + i] = ws[i];
            ws[12 * LumaStride + i] = ws[i];
        }

        // Left column
        if (mbx == 0)
        {
            for (int i = 0; i < 16; i++)
                ws[(i + 1) * LumaStride] = 129;
        }
        else
        {
            for (int i = 0; i < 16; i++)
                ws[(i + 1) * LumaStride] = left[1 + i];
        }

        // Top-left pixel
        ws[0] = mby == 0 ? (byte)127 : (mbx == 0 ? (byte)129 : left[0]);

        return ws;
    }

    public static byte[] CreateBorderChroma(int mbx, int mby, byte[] top, byte[] left)
    {
        byte[] block = new byte[ChromaBlockSize];

        // Above row
        if (mby == 0)
        {
            for (int i = 1; i < ChromaStride; i++)
                block[i] = 127;
        }
        else
        {
            for (int i = 0; i < 8; i++)
                block[1 + i] = top[mbx * 8 + i];
        }

        // Left column
        if (mbx == 0)
        {
            for (int i = 0; i < 8; i++)
                block[(i + 1) * ChromaStride] = 129;
        }
        else
        {
            for (int i = 0; i < 8; i++)
                block[(i + 1) * ChromaStride] = left[1 + i];
        }

        // Top-left pixel
        block[0] = mby == 0 ? (byte)127 : (mbx == 0 ? (byte)129 : left[0]);

        return block;
    }

    public static void AddResidue(byte[] block, int[] residue, int resOffset, int y0, int x0, int stride)
    {
        int pos = y0 * stride + x0;
        for (int row = 0; row < 4; row++)
        {
            for (int col = 0; col < 4; col++)
            {
                int val = block[pos + col] + residue[resOffset + row * 4 + col];
                block[pos + col] = (byte)Math.Max(0, Math.Min(255, val));
            }
            pos += stride;
        }
    }

    private static byte Avg3(byte left, byte curr, byte right)
    {
        return (byte)((left + 2 * curr + right + 2) >> 2);
    }

    private static byte Avg2(byte a, byte b)
    {
        return (byte)((a + b + 1) >> 1);
    }

    public static void PredictVPred(byte[] a, int size, int x0, int y0, int stride)
    {
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                a[(y0 + y) * stride + x0 + x] = a[(y0 - 1) * stride + x0 + x];
            }
        }
    }

    public static void PredictHPred(byte[] a, int size, int x0, int y0, int stride)
    {
        for (int y = 0; y < size; y++)
        {
            byte left = a[(y0 + y) * stride + x0 - 1];
            for (int x = 0; x < size; x++)
            {
                a[(y0 + y) * stride + x0 + x] = left;
            }
        }
    }

    public static void PredictDcPred(byte[] a, int size, int stride, bool above, bool left)
    {
        uint sum = 0;
        int shf = size == 8 ? 2 : 3;

        if (left)
        {
            for (int y = 0; y < size; y++)
                sum += a[(y + 1) * stride];
            shf++;
        }

        if (above)
        {
            for (int x = 1; x <= size; x++)
                sum += a[x];
            shf++;
        }

        byte dcval = (!left && !above) ? (byte)128 : (byte)((sum + (1u << (shf - 1))) >> shf);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                a[1 + stride * (y + 1) + x] = dcval;
            }
        }
    }

    public static void PredictTmPred(byte[] a, int size, int x0, int y0, int stride)
    {
        int p = a[(y0 - 1) * stride + x0 - 1];

        for (int y = 0; y < size; y++)
        {
            int leftMinusP = a[(y0 + y) * stride + x0 - 1] - p;
            for (int x = 0; x < size; x++)
            {
                int val = leftMinusP + a[(y0 - 1) * stride + x0 + x];
                a[(y0 + y) * stride + x0 + x] = (byte)Math.Max(0, Math.Min(255, val));
            }
        }
    }

    public static void Predict4x4(byte[] ws, int stride, IntraMode[] modes, int[] resdata)
    {
        for (int sby = 0; sby < 4; sby++)
        {
            for (int sbx = 0; sbx < 4; sbx++)
            {
                int i = sbx + sby * 4;
                int y0 = sby * 4 + 1;
                int x0 = sbx * 4 + 1;

                switch (modes[i])
                {
                    case IntraMode.TM: PredictTmPred(ws, 4, x0, y0, stride); break;
                    case IntraMode.VE: PredictBVePred(ws, x0, y0, stride); break;
                    case IntraMode.HE: PredictBHePred(ws, x0, y0, stride); break;
                    case IntraMode.DC: PredictBDcPred(ws, x0, y0, stride); break;
                    case IntraMode.LD: PredictBLdPred(ws, x0, y0, stride); break;
                    case IntraMode.RD: PredictBRdPred(ws, x0, y0, stride); break;
                    case IntraMode.VR: PredictBVrPred(ws, x0, y0, stride); break;
                    case IntraMode.VL: PredictBVlPred(ws, x0, y0, stride); break;
                    case IntraMode.HD: PredictBHdPred(ws, x0, y0, stride); break;
                    case IntraMode.HU: PredictBHuPred(ws, x0, y0, stride); break;
                }

                AddResidue(ws, resdata, i * 16, y0, x0, stride);
            }
        }
    }

    private static void PredictBDcPred(byte[] a, int x0, int y0, int stride)
    {
        uint v = 4;
        for (int x = 0; x < 4; x++)
            v += a[(y0 - 1) * stride + x0 + x];
        for (int y = 0; y < 4; y++)
            v += a[(y0 + y) * stride + x0 - 1];
        v >>= 3;

        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                a[(y0 + y) * stride + x0 + x] = (byte)v;
    }

    private static void PredictBVePred(byte[] a, int x0, int y0, int stride)
    {
        byte p = a[(y0 - 1) * stride + x0 - 1];
        byte a0 = a[(y0 - 1) * stride + x0];
        byte a1 = a[(y0 - 1) * stride + x0 + 1];
        byte a2 = a[(y0 - 1) * stride + x0 + 2];
        byte a3 = a[(y0 - 1) * stride + x0 + 3];
        byte a4 = a[(y0 - 1) * stride + x0 + 4];

        byte[] avg = { Avg3(p, a0, a1), Avg3(a0, a1, a2), Avg3(a1, a2, a3), Avg3(a2, a3, a4) };

        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                a[(y0 + y) * stride + x0 + x] = avg[x];
    }

    private static void PredictBHePred(byte[] a, int x0, int y0, int stride)
    {
        byte p = a[(y0 - 1) * stride + x0 - 1];
        byte l0 = a[y0 * stride + x0 - 1];
        byte l1 = a[(y0 + 1) * stride + x0 - 1];
        byte l2 = a[(y0 + 2) * stride + x0 - 1];
        byte l3 = a[(y0 + 3) * stride + x0 - 1];

        byte[] avgs = { Avg3(p, l0, l1), Avg3(l0, l1, l2), Avg3(l1, l2, l3), Avg3(l2, l3, l3) };

        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                a[(y0 + y) * stride + x0 + x] = avgs[y];
    }

    private static void PredictBLdPred(byte[] a, int x0, int y0, int stride)
    {
        byte a0 = a[(y0 - 1) * stride + x0];
        byte a1 = a[(y0 - 1) * stride + x0 + 1];
        byte a2 = a[(y0 - 1) * stride + x0 + 2];
        byte a3 = a[(y0 - 1) * stride + x0 + 3];
        byte a4 = a[(y0 - 1) * stride + x0 + 4];
        byte a5 = a[(y0 - 1) * stride + x0 + 5];
        byte a6 = a[(y0 - 1) * stride + x0 + 6];
        byte a7 = a[(y0 - 1) * stride + x0 + 7];

        byte[] avgs = {
            Avg3(a0, a1, a2), Avg3(a1, a2, a3), Avg3(a2, a3, a4), Avg3(a3, a4, a5),
            Avg3(a4, a5, a6), Avg3(a5, a6, a7), Avg3(a6, a7, a7)
        };

        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                a[(y0 + y) * stride + x0 + x] = avgs[y + x];
    }

    private static void PredictBRdPred(byte[] a, int x0, int y0, int stride)
    {
        var (e0, e1, e2, e3, e4, e5, e6, e7, e8) = GetEdgePixels(a, x0, y0, stride);

        byte[] avgs = {
            Avg3(e0, e1, e2), Avg3(e1, e2, e3), Avg3(e2, e3, e4), Avg3(e3, e4, e5),
            Avg3(e4, e5, e6), Avg3(e5, e6, e7), Avg3(e6, e7, e8)
        };

        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                a[(y0 + y) * stride + x0 + x] = avgs[3 - y + x];
    }

    private static void PredictBVrPred(byte[] a, int x0, int y0, int stride)
    {
        var (_, e1, e2, e3, e4, e5, e6, e7, e8) = GetEdgePixels(a, x0, y0, stride);

        a[(y0 + 3) * stride + x0] = Avg3(e1, e2, e3);
        a[(y0 + 2) * stride + x0] = Avg3(e2, e3, e4);
        a[(y0 + 3) * stride + x0 + 1] = Avg3(e3, e4, e5);
        a[(y0 + 1) * stride + x0] = Avg3(e3, e4, e5);
        a[(y0 + 2) * stride + x0 + 1] = Avg2(e4, e5);
        a[y0 * stride + x0] = Avg2(e4, e5);
        a[(y0 + 3) * stride + x0 + 2] = Avg3(e4, e5, e6);
        a[(y0 + 1) * stride + x0 + 1] = Avg3(e4, e5, e6);
        a[(y0 + 2) * stride + x0 + 2] = Avg2(e5, e6);
        a[y0 * stride + x0 + 1] = Avg2(e5, e6);
        a[(y0 + 3) * stride + x0 + 3] = Avg3(e5, e6, e7);
        a[(y0 + 1) * stride + x0 + 2] = Avg3(e5, e6, e7);
        a[(y0 + 2) * stride + x0 + 3] = Avg2(e6, e7);
        a[y0 * stride + x0 + 2] = Avg2(e6, e7);
        a[(y0 + 1) * stride + x0 + 3] = Avg3(e6, e7, e8);
        a[y0 * stride + x0 + 3] = Avg2(e7, e8);
    }

    private static void PredictBVlPred(byte[] a, int x0, int y0, int stride)
    {
        byte a0 = a[(y0 - 1) * stride + x0];
        byte a1 = a[(y0 - 1) * stride + x0 + 1];
        byte a2 = a[(y0 - 1) * stride + x0 + 2];
        byte a3 = a[(y0 - 1) * stride + x0 + 3];
        byte a4 = a[(y0 - 1) * stride + x0 + 4];
        byte a5 = a[(y0 - 1) * stride + x0 + 5];
        byte a6 = a[(y0 - 1) * stride + x0 + 6];
        byte a7 = a[(y0 - 1) * stride + x0 + 7];

        a[y0 * stride + x0] = Avg2(a0, a1);
        a[(y0 + 1) * stride + x0] = Avg3(a0, a1, a2);
        a[(y0 + 2) * stride + x0] = Avg2(a1, a2);
        a[y0 * stride + x0 + 1] = Avg2(a1, a2);
        a[(y0 + 1) * stride + x0 + 1] = Avg3(a1, a2, a3);
        a[(y0 + 3) * stride + x0] = Avg3(a1, a2, a3);
        a[(y0 + 2) * stride + x0 + 1] = Avg2(a2, a3);
        a[y0 * stride + x0 + 2] = Avg2(a2, a3);
        a[(y0 + 3) * stride + x0 + 1] = Avg3(a2, a3, a4);
        a[(y0 + 1) * stride + x0 + 2] = Avg3(a2, a3, a4);
        a[(y0 + 2) * stride + x0 + 2] = Avg2(a3, a4);
        a[y0 * stride + x0 + 3] = Avg2(a3, a4);
        a[(y0 + 3) * stride + x0 + 2] = Avg3(a3, a4, a5);
        a[(y0 + 1) * stride + x0 + 3] = Avg3(a3, a4, a5);
        a[(y0 + 2) * stride + x0 + 3] = Avg3(a4, a5, a6);
        a[(y0 + 3) * stride + x0 + 3] = Avg3(a5, a6, a7);
    }

    private static void PredictBHdPred(byte[] a, int x0, int y0, int stride)
    {
        var (e0, e1, e2, e3, e4, e5, e6, e7, _) = GetEdgePixels(a, x0, y0, stride);

        a[(y0 + 3) * stride + x0] = Avg2(e0, e1);
        a[(y0 + 3) * stride + x0 + 1] = Avg3(e0, e1, e2);
        a[(y0 + 2) * stride + x0] = Avg2(e1, e2);
        a[(y0 + 3) * stride + x0 + 2] = Avg2(e1, e2);
        a[(y0 + 2) * stride + x0 + 1] = Avg3(e1, e2, e3);
        a[(y0 + 3) * stride + x0 + 3] = Avg3(e1, e2, e3);
        a[(y0 + 2) * stride + x0 + 2] = Avg2(e2, e3);
        a[(y0 + 1) * stride + x0] = Avg2(e2, e3);
        a[(y0 + 2) * stride + x0 + 3] = Avg3(e2, e3, e4);
        a[(y0 + 1) * stride + x0 + 1] = Avg3(e2, e3, e4);
        a[(y0 + 1) * stride + x0 + 2] = Avg2(e3, e4);
        a[y0 * stride + x0] = Avg2(e3, e4);
        a[(y0 + 1) * stride + x0 + 3] = Avg3(e3, e4, e5);
        a[y0 * stride + x0 + 1] = Avg3(e3, e4, e5);
        a[y0 * stride + x0 + 2] = Avg3(e4, e5, e6);
        a[y0 * stride + x0 + 3] = Avg3(e5, e6, e7);
    }

    private static void PredictBHuPred(byte[] a, int x0, int y0, int stride)
    {
        byte l0 = a[y0 * stride + x0 - 1];
        byte l1 = a[(y0 + 1) * stride + x0 - 1];
        byte l2 = a[(y0 + 2) * stride + x0 - 1];
        byte l3 = a[(y0 + 3) * stride + x0 - 1];

        a[y0 * stride + x0] = Avg2(l0, l1);
        a[y0 * stride + x0 + 1] = Avg3(l0, l1, l2);
        a[y0 * stride + x0 + 2] = Avg2(l1, l2);
        a[(y0 + 1) * stride + x0] = Avg2(l1, l2);
        a[y0 * stride + x0 + 3] = Avg3(l1, l2, l3);
        a[(y0 + 1) * stride + x0 + 1] = Avg3(l1, l2, l3);
        a[(y0 + 1) * stride + x0 + 2] = Avg2(l2, l3);
        a[(y0 + 2) * stride + x0] = Avg2(l2, l3);
        a[(y0 + 1) * stride + x0 + 3] = Avg3(l2, l3, l3);
        a[(y0 + 2) * stride + x0 + 1] = Avg3(l2, l3, l3);
        a[(y0 + 2) * stride + x0 + 2] = l3;
        a[(y0 + 2) * stride + x0 + 3] = l3;
        a[(y0 + 3) * stride + x0] = l3;
        a[(y0 + 3) * stride + x0 + 1] = l3;
        a[(y0 + 3) * stride + x0 + 2] = l3;
        a[(y0 + 3) * stride + x0 + 3] = l3;
    }

    private static (byte, byte, byte, byte, byte, byte, byte, byte, byte) GetEdgePixels(
        byte[] a, int x0, int y0, int stride)
    {
        int pos = (y0 - 1) * stride + x0 - 1;
        return (
            a[pos + 4 * stride],
            a[pos + 3 * stride],
            a[pos + 2 * stride],
            a[pos + stride],
            a[pos],
            a[pos + 1],
            a[pos + 2],
            a[pos + 3],
            a[pos + 4]
        );
    }

    public static void SetChromaBorder(byte[] leftBorder, byte[] topBorder, byte[] chromaBlock, int mbx)
    {
        leftBorder[0] = chromaBlock[8];
        for (int i = 0; i < 8; i++)
            leftBorder[1 + i] = chromaBlock[(i + 1) * ChromaStride + 8];
        for (int i = 0; i < 8; i++)
            topBorder[mbx * 8 + i] = chromaBlock[8 * ChromaStride + 1 + i];
    }
}
