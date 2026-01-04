using System;

namespace TinyImage.Codecs.WebP.Lossless;

/// <summary>
/// Types of transforms used in VP8L lossless compression.
/// </summary>
internal enum TransformKind
{
    Predictor = 0,
    Color = 1,
    SubtractGreen = 2,
    ColorIndexing = 3
}

/// <summary>
/// Represents a VP8L transform.
/// </summary>
internal class LosslessTransform
{
    public TransformKind Kind { get; set; }
    public byte SizeBits { get; set; }
    public byte[] Data { get; set; }
    public ushort TableSize { get; set; }
}

/// <summary>
/// Implements VP8L lossless transforms.
/// Translated from webp-rust lossless_transform.rs
/// </summary>
internal static class LosslessTransformApply
{
    /// <summary>
    /// Computes the subsample size for transform block dimensions.
    /// </summary>
    public static ushort SubsampleSize(ushort size, byte bits)
    {
        return (ushort)(((uint)size + (1u << bits) - 1) >> bits);
    }

    /// <summary>
    /// Applies the predictor transform to the image data.
    /// </summary>
    public static void ApplyPredictorTransform(byte[] imageData, ushort width, ushort height,
        byte sizeBits, byte[] predictorData)
    {
        int blockXSize = SubsampleSize(width, sizeBits);
        int widthInt = width;
        int heightInt = height;

        // Handle top-left pixel specially (predictor 0: ARGB black)
        imageData[3] = (byte)(imageData[3] + 255);

        // Top row: use predictor 1 (L)
        ApplyPredictorTransform1(imageData, 4, widthInt * 4, widthInt);

        // Left column: use predictor 2 (T)
        for (int y = 1; y < heightInt; y++)
        {
            for (int i = 0; i < 4; i++)
            {
                imageData[y * widthInt * 4 + i] = (byte)(imageData[y * widthInt * 4 + i] +
                    imageData[(y - 1) * widthInt * 4 + i]);
            }
        }

        // Rest of the image: use predictor from transform data
        for (int y = 1; y < heightInt; y++)
        {
            for (int blockX = 0; blockX < blockXSize; blockX++)
            {
                int blockIndex = (y >> sizeBits) * blockXSize + blockX;
                byte predictor = predictorData[blockIndex * 4 + 1];
                int startIndex = (y * widthInt + Math.Max(blockX << sizeBits, 1)) * 4;
                int endIndex = (y * widthInt + Math.Min((blockX + 1) << sizeBits, widthInt)) * 4;

                ApplyPredictor(imageData, startIndex, endIndex, widthInt, predictor);
            }
        }
    }

    private static void ApplyPredictor(byte[] data, int start, int end, int width, byte predictor)
    {
        switch (predictor)
        {
            case 0: ApplyPredictorTransform0(data, start, end, width); break;
            case 1: ApplyPredictorTransform1(data, start, end, width); break;
            case 2: ApplyPredictorTransform2(data, start, end, width); break;
            case 3: ApplyPredictorTransform3(data, start, end, width); break;
            case 4: ApplyPredictorTransform4(data, start, end, width); break;
            case 5: ApplyPredictorTransform5(data, start, end, width); break;
            case 6: ApplyPredictorTransform6(data, start, end, width); break;
            case 7: ApplyPredictorTransform7(data, start, end, width); break;
            case 8: ApplyPredictorTransform8(data, start, end, width); break;
            case 9: ApplyPredictorTransform9(data, start, end, width); break;
            case 10: ApplyPredictorTransform10(data, start, end, width); break;
            case 11: ApplyPredictorTransform11(data, start, end, width); break;
            case 12: ApplyPredictorTransform12(data, start, end, width); break;
            case 13: ApplyPredictorTransform13(data, start, end, width); break;
        }
    }

    // Predictor 0: ARGB black with alpha 255
    private static void ApplyPredictorTransform0(byte[] data, int start, int end, int width)
    {
        for (int i = start + 3; i < end; i += 4)
            data[i] = (byte)(data[i] + 0xff);
    }

    // Predictor 1: L (left pixel)
    private static void ApplyPredictorTransform1(byte[] data, int start, int end, int width)
    {
        for (int i = start; i < end; i++)
            data[i] = (byte)(data[i] + data[i - 4]);
    }

    // Predictor 2: T (top pixel)
    private static void ApplyPredictorTransform2(byte[] data, int start, int end, int width)
    {
        for (int i = start; i < end; i++)
            data[i] = (byte)(data[i] + data[i - width * 4]);
    }

    // Predictor 3: TR (top-right pixel)
    private static void ApplyPredictorTransform3(byte[] data, int start, int end, int width)
    {
        for (int i = start; i < end; i++)
            data[i] = (byte)(data[i] + data[i - width * 4 + 4]);
    }

    // Predictor 4: TL (top-left pixel)
    private static void ApplyPredictorTransform4(byte[] data, int start, int end, int width)
    {
        for (int i = start; i < end; i++)
            data[i] = (byte)(data[i] + data[i - width * 4 - 4]);
    }

    // Predictor 5: Average2(Average2(L, TR), T)
    private static void ApplyPredictorTransform5(byte[] data, int start, int end, int width)
    {
        byte[] prev = new byte[4];
        Array.Copy(data, start - 4, prev, 0, 4);

        for (int i = start; i < end; i += 4)
        {
            int tr = i - width * 4 + 4;
            int t = i - width * 4;

            for (int j = 0; j < 4; j++)
            {
                prev[j] = (byte)(data[i + j] + Average2(Average2(prev[j], data[tr + j]), data[t + j]));
                data[i + j] = prev[j];
            }
        }
    }

    // Predictor 6: Average2(L, TL)
    private static void ApplyPredictorTransform6(byte[] data, int start, int end, int width)
    {
        for (int i = start; i < end; i++)
            data[i] = (byte)(data[i] + Average2(data[i - 4], data[i - width * 4 - 4]));
    }

    // Predictor 7: Average2(L, T)
    private static void ApplyPredictorTransform7(byte[] data, int start, int end, int width)
    {
        byte[] prev = new byte[4];
        Array.Copy(data, start - 4, prev, 0, 4);

        for (int i = start; i < end; i += 4)
        {
            int t = i - width * 4;
            for (int j = 0; j < 4; j++)
            {
                prev[j] = (byte)(data[i + j] + Average2(prev[j], data[t + j]));
                data[i + j] = prev[j];
            }
        }
    }

    // Predictor 8: Average2(TL, T)
    private static void ApplyPredictorTransform8(byte[] data, int start, int end, int width)
    {
        for (int i = start; i < end; i++)
            data[i] = (byte)(data[i] + Average2(data[i - width * 4 - 4], data[i - width * 4]));
    }

    // Predictor 9: Average2(T, TR)
    private static void ApplyPredictorTransform9(byte[] data, int start, int end, int width)
    {
        for (int i = start; i < end; i++)
            data[i] = (byte)(data[i] + Average2(data[i - width * 4], data[i - width * 4 + 4]));
    }

    // Predictor 10: Average2(Average2(L, TL), Average2(T, TR))
    private static void ApplyPredictorTransform10(byte[] data, int start, int end, int width)
    {
        byte[] prev = new byte[4];
        Array.Copy(data, start - 4, prev, 0, 4);

        for (int i = start; i < end; i += 4)
        {
            int tl = i - width * 4 - 4;
            int t = i - width * 4;
            int tr = i - width * 4 + 4;

            for (int j = 0; j < 4; j++)
            {
                prev[j] = (byte)(data[i + j] + Average2(
                    Average2(prev[j], data[tl + j]),
                    Average2(data[t + j], data[tr + j])));
                data[i + j] = prev[j];
            }
        }
    }

    // Predictor 11: Select(L, T, TL)
    private static void ApplyPredictorTransform11(byte[] data, int start, int end, int width)
    {
        short[] l = new short[4];
        short[] tl = new short[4];

        for (int j = 0; j < 4; j++)
        {
            l[j] = data[start - 4 + j];
            tl[j] = data[start - width * 4 - 4 + j];
        }

        for (int i = start; i < end; i += 4)
        {
            short[] t = new short[4];
            for (int j = 0; j < 4; j++)
                t[j] = data[i - width * 4 + j];

            int predictLeft = 0;
            int predictTop = 0;
            for (int j = 0; j < 4; j++)
            {
                int predict = l[j] + t[j] - tl[j];
                predictLeft += Math.Abs(predict - l[j]);
                predictTop += Math.Abs(predict - t[j]);
            }

            if (predictLeft < predictTop)
            {
                for (int j = 0; j < 4; j++)
                    data[i + j] = (byte)(data[i + j] + l[j]);
            }
            else
            {
                for (int j = 0; j < 4; j++)
                    data[i + j] = (byte)(data[i + j] + t[j]);
            }

            for (int j = 0; j < 4; j++)
            {
                tl[j] = t[j];
                l[j] = data[i + j];
            }
        }
    }

    // Predictor 12: ClampAddSubtractFull(L, T, TL)
    private static void ApplyPredictorTransform12(byte[] data, int start, int end, int width)
    {
        byte[] prev = new byte[4];
        Array.Copy(data, start - 4, prev, 0, 4);

        for (int i = start; i < end; i += 4)
        {
            int tl = i - width * 4 - 4;
            int t = i - width * 4;

            for (int j = 0; j < 4; j++)
            {
                prev[j] = (byte)(data[i + j] + ClampAddSubtractFull(prev[j], data[t + j], data[tl + j]));
                data[i + j] = prev[j];
            }
        }
    }

    // Predictor 13: ClampAddSubtractHalf(Average2(L, T), TL)
    private static void ApplyPredictorTransform13(byte[] data, int start, int end, int width)
    {
        byte[] prev = new byte[4];
        Array.Copy(data, start - 4, prev, 0, 4);

        for (int i = start; i < end; i += 4)
        {
            int tl = i - width * 4 - 4;
            int t = i - width * 4;

            for (int j = 0; j < 4; j++)
            {
                short avg = (short)((prev[j] + data[t + j]) / 2);
                prev[j] = (byte)(data[i + j] + ClampAddSubtractHalf(avg, data[tl + j]));
                data[i + j] = prev[j];
            }
        }
    }

    private static byte Average2(byte a, byte b) => (byte)(((ushort)a + b) / 2);

    private static byte ClampAddSubtractFull(int a, int b, int c) =>
        (byte)Math.Max(0, Math.Min(255, a + b - c));

    private static byte ClampAddSubtractHalf(int a, int b) =>
        (byte)Math.Max(0, Math.Min(255, a + (a - b) / 2));

    /// <summary>
    /// Applies the color transform to the image data.
    /// </summary>
    public static void ApplyColorTransform(byte[] imageData, ushort width, byte sizeBits, byte[] transformData)
    {
        int blockXSize = SubsampleSize(width, sizeBits);
        int widthInt = width;

        int numRows = imageData.Length / (widthInt * 4);
        for (int y = 0; y < numRows; y++)
        {
            int rowTransformDataStart = (y >> sizeBits) * blockXSize * 4;

            for (int blockX = 0; blockX < blockXSize; blockX++)
            {
                int transformOffset = rowTransformDataStart + blockX * 4;
                byte redToBlue = transformData[transformOffset];
                byte greenToBlue = transformData[transformOffset + 1];
                byte greenToRed = transformData[transformOffset + 2];

                int startX = blockX << sizeBits;
                int endX = Math.Min((blockX + 1) << sizeBits, widthInt);

                for (int x = startX; x < endX; x++)
                {
                    int pixelOffset = (y * widthInt + x) * 4;
                    uint green = imageData[pixelOffset + 1];
                    uint tempRed = imageData[pixelOffset];
                    uint tempBlue = imageData[pixelOffset + 2];

                    tempRed += ColorTransformDelta((sbyte)greenToRed, (sbyte)green);
                    tempBlue += ColorTransformDelta((sbyte)greenToBlue, (sbyte)green);
                    tempBlue += ColorTransformDelta((sbyte)redToBlue, (sbyte)tempRed);

                    imageData[pixelOffset] = (byte)(tempRed & 0xff);
                    imageData[pixelOffset + 2] = (byte)(tempBlue & 0xff);
                }
            }
        }
    }

    private static uint ColorTransformDelta(sbyte t, sbyte c) =>
        (uint)((t * c) >> 5);

    /// <summary>
    /// Applies the subtract green transform (adds green back to red and blue).
    /// </summary>
    public static void ApplySubtractGreenTransform(byte[] imageData)
    {
        for (int i = 0; i < imageData.Length; i += 4)
        {
            imageData[i] = (byte)(imageData[i] + imageData[i + 1]);
            imageData[i + 2] = (byte)(imageData[i + 2] + imageData[i + 1]);
        }
    }

    /// <summary>
    /// Applies the color indexing transform.
    /// </summary>
    public static void ApplyColorIndexingTransform(byte[] imageData, ushort width, ushort height,
        ushort tableSize, byte[] tableData)
    {
        if (tableSize == 0)
            return;

        if (tableSize > 16)
        {
            // Large palette: simple 1:1 index lookup
            ApplyColorIndexingLargePalette(imageData, width, height, tableSize, tableData);
        }
        else
        {
            // Small palette: packed pixels
            byte bitsPerPixel;
            if (tableSize <= 2)
                bitsPerPixel = 1;
            else if (tableSize <= 4)
                bitsPerPixel = 2;
            else
                bitsPerPixel = 4;

            ApplyColorIndexingSmallPalette(imageData, width, height, tableSize, tableData, bitsPerPixel);
        }
    }

    private static void ApplyColorIndexingLargePalette(byte[] imageData, ushort width, ushort height,
        ushort tableSize, byte[] tableData)
    {
        // Pad table to 256 entries
        byte[][] table = new byte[256][];
        for (int i = 0; i < 256; i++)
        {
            if (i < tableSize)
            {
                table[i] = new byte[4];
                Array.Copy(tableData, i * 4, table[i], 0, 4);
            }
            else
            {
                table[i] = new byte[4]; // Black transparent
            }
        }

        int numPixels = width * height;
        for (int i = 0; i < numPixels; i++)
        {
            // Index is in green channel
            byte index = imageData[i * 4 + 1];
            Array.Copy(table[index], 0, imageData, i * 4, 4);
        }
    }

    private static void ApplyColorIndexingSmallPalette(byte[] imageData, ushort width, ushort height,
        ushort tableSize, byte[] tableData, byte bitsPerPixel)
    {
        byte pixelsPerByte = (byte)(8 / bitsPerPixel);
        byte mask = (byte)((1 << bitsPerPixel) - 1);

        int packedWidth = (width + pixelsPerByte - 1) / pixelsPerByte;
        int outputStride = width * 4;
        int inputStride = packedWidth * 4;

        // Process from bottom to top to avoid overwriting data we still need
        for (int yRev = 0; yRev < height; yRev++)
        {
            int y = height - 1 - yRev;
            byte[] packedIndices = new byte[packedWidth];

            // Read packed indices for this row
            for (int px = 0; px < packedWidth; px++)
                packedIndices[px] = imageData[y * inputStride + px * 4 + 1];

            // Expand to full pixels
            for (int x = 0; x < width; x++)
            {
                int packedX = x / pixelsPerByte;
                int subIndex = x % pixelsPerByte;
                byte packedValue = packedIndices[packedX];
                byte colorIndex = (byte)((packedValue >> (subIndex * bitsPerPixel)) & mask);

                int outputOffset = y * outputStride + x * 4;
                if (colorIndex < tableSize)
                    Array.Copy(tableData, colorIndex * 4, imageData, outputOffset, 4);
                else
                    Array.Clear(imageData, outputOffset, 4);
            }
        }
    }

    /// <summary>
    /// Adjusts the color map (subtraction decoding).
    /// </summary>
    public static void AdjustColorMap(byte[] colorMap)
    {
        for (int i = 4; i < colorMap.Length; i++)
            colorMap[i] = (byte)(colorMap[i] + colorMap[i - 4]);
    }
}
