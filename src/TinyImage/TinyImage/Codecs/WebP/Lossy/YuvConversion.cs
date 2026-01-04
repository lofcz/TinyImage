using System;

namespace TinyImage.Codecs.WebP.Lossy;

/// <summary>
/// YUV to RGB conversion utilities for VP8.
/// Translated from webp-rust yuv.rs
/// </summary>
internal static class YuvConversion
{
    private const int YuvFix2 = 6;
    private const int YuvFix = 16;
    private const int YuvHalf = 1 << (YuvFix - 1);

    /// <summary>
    /// Multiplies and shifts for YUV conversion.
    /// </summary>
    private static int Mulhi(byte v, ushort coeff)
    {
        return (int)(((uint)v * coeff) >> 8);
    }

    /// <summary>
    /// Clips value to 0-255 range.
    /// </summary>
    private static byte Clip(int v)
    {
        return (byte)Math.Max(0, Math.Min(255, v >> YuvFix2));
    }

    private static byte YuvToR(byte y, byte v)
    {
        return Clip(Mulhi(y, 19077) + Mulhi(v, 26149) - 14234);
    }

    private static byte YuvToG(byte y, byte u, byte v)
    {
        return Clip(Mulhi(y, 19077) - Mulhi(u, 6419) - Mulhi(v, 13320) + 8708);
    }

    private static byte YuvToB(byte y, byte u)
    {
        return Clip(Mulhi(y, 19077) + Mulhi(u, 33050) - 17685);
    }

    /// <summary>
    /// Fills an RGBA buffer from YUV buffers using bilinear interpolation.
    /// </summary>
    public static void FillRgbaBufferFancy(byte[] buffer, byte[] yBuffer, byte[] uBuffer, byte[] vBuffer,
        int width, int height, int bufferWidth)
    {
        int chromaBufferWidth = bufferWidth / 2;
        int chromaWidth = (width + 1) / 2;

        // Top row
        FillRowFancyWith1UvRow(buffer, 0, yBuffer, 0, uBuffer, 0, vBuffer, 0, width, chromaWidth);

        int rowOffset = width * 4;
        int yRowOffset = bufferWidth;
        int uvRowOffset = chromaBufferWidth;

        // Middle rows (2 at a time)
        for (int y = 1; y < height - 1; y += 2)
        {
            int bufOffset = y * width * 4;
            int yOffset = y * bufferWidth;
            int uvOffset = (y / 2) * chromaBufferWidth;

            FillRowFancyWith2UvRows(buffer, bufOffset, yBuffer, yOffset,
                uBuffer, uvOffset, uBuffer, uvOffset + chromaBufferWidth,
                vBuffer, uvOffset, vBuffer, uvOffset + chromaBufferWidth,
                width, chromaWidth, bufferWidth);

            FillRowFancyWith2UvRows(buffer, bufOffset + rowOffset, yBuffer, yOffset + bufferWidth,
                uBuffer, uvOffset + chromaBufferWidth, uBuffer, uvOffset,
                vBuffer, uvOffset + chromaBufferWidth, vBuffer, uvOffset,
                width, chromaWidth, bufferWidth);
        }

        // Bottom row if height is even
        if (height % 2 == 0)
        {
            int y = height - 1;
            int chromaHeight = (height + 1) / 2;
            int uvOffset = (chromaHeight - 1) * chromaBufferWidth;
            FillRowFancyWith1UvRow(buffer, y * width * 4, yBuffer, y * bufferWidth,
                uBuffer, uvOffset, vBuffer, uvOffset, width, chromaWidth);
        }
    }

    private static void FillRowFancyWith1UvRow(byte[] buffer, int bufOffset,
        byte[] yRow, int yOffset, byte[] uRow, int uOffset, byte[] vRow, int vOffset,
        int width, int chromaWidth)
    {
        // First pixel
        SetPixel(buffer, bufOffset, yRow[yOffset], uRow[uOffset], vRow[vOffset]);

        // Two pixels at a time
        for (int x = 1; x < width - 1; x += 2)
        {
            int uvIdx = x / 2;
            byte u0 = uRow[uOffset + uvIdx];
            byte u1 = uvIdx + 1 < chromaWidth ? uRow[uOffset + uvIdx + 1] : u0;
            byte v0 = vRow[vOffset + uvIdx];
            byte v1 = uvIdx + 1 < chromaWidth ? vRow[vOffset + uvIdx + 1] : v0;

            byte u = GetFancyChromaValue(u0, u1, u0, u1);
            byte v = GetFancyChromaValue(v0, v1, v0, v1);
            SetPixel(buffer, bufOffset + x * 4, yRow[yOffset + x], u, v);

            if (x + 1 < width)
            {
                u = GetFancyChromaValue(u1, u0, u1, u0);
                v = GetFancyChromaValue(v1, v0, v1, v0);
                SetPixel(buffer, bufOffset + (x + 1) * 4, yRow[yOffset + x + 1], u, v);
            }
        }
    }

    private static void FillRowFancyWith2UvRows(byte[] buffer, int bufOffset, byte[] yRow, int yOffset,
        byte[] uRow1, int uOffset1, byte[] uRow2, int uOffset2,
        byte[] vRow1, int vOffset1, byte[] vRow2, int vOffset2,
        int width, int chromaWidth, int bufferWidth)
    {
        // First pixel
        byte u = GetFancyChromaValue(uRow1[uOffset1], uRow1[uOffset1], uRow2[uOffset2], uRow2[uOffset2]);
        byte v = GetFancyChromaValue(vRow1[vOffset1], vRow1[vOffset1], vRow2[vOffset2], vRow2[vOffset2]);
        SetPixel(buffer, bufOffset, yRow[yOffset], u, v);

        // Two pixels at a time
        for (int x = 1; x < width - 1; x += 2)
        {
            int uvIdx = x / 2;
            byte u10 = uRow1[uOffset1 + uvIdx];
            byte u11 = uvIdx + 1 < chromaWidth ? uRow1[uOffset1 + uvIdx + 1] : u10;
            byte u20 = uRow2[uOffset2 + uvIdx];
            byte u21 = uvIdx + 1 < chromaWidth ? uRow2[uOffset2 + uvIdx + 1] : u20;

            byte v10 = vRow1[vOffset1 + uvIdx];
            byte v11 = uvIdx + 1 < chromaWidth ? vRow1[vOffset1 + uvIdx + 1] : v10;
            byte v20 = vRow2[vOffset2 + uvIdx];
            byte v21 = uvIdx + 1 < chromaWidth ? vRow2[vOffset2 + uvIdx + 1] : v20;

            u = GetFancyChromaValue(u10, u11, u20, u21);
            v = GetFancyChromaValue(v10, v11, v20, v21);
            SetPixel(buffer, bufOffset + x * 4, yRow[yOffset + x], u, v);

            if (x + 1 < width)
            {
                u = GetFancyChromaValue(u11, u10, u21, u20);
                v = GetFancyChromaValue(v11, v10, v21, v20);
                SetPixel(buffer, bufOffset + (x + 1) * 4, yRow[yOffset + x + 1], u, v);
            }
        }
    }

    private static byte GetFancyChromaValue(byte main, byte secondary1, byte secondary2, byte tertiary)
    {
        return (byte)((9 * main + 3 * secondary1 + 3 * secondary2 + tertiary + 8) / 16);
    }

    private static void SetPixel(byte[] buffer, int offset, byte y, byte u, byte v)
    {
        buffer[offset] = YuvToR(y, v);
        buffer[offset + 1] = YuvToG(y, u, v);
        buffer[offset + 2] = YuvToB(y, u);
        buffer[offset + 3] = 255;
    }

    /// <summary>
    /// Simple YUV to RGBA conversion without fancy upsampling.
    /// </summary>
    public static void FillRgbaBufferSimple(byte[] buffer, byte[] yBuffer, byte[] uBuffer, byte[] vBuffer,
        int width, int chromaWidth, int bufferWidth)
    {
        int height = buffer.Length / (width * 4);
        int chromaBufferWidth = bufferWidth / 2;

        for (int y = 0; y < height; y++)
        {
            int bufOffset = y * width * 4;
            int yOffset = y * bufferWidth;
            int uvOffset = (y / 2) * chromaBufferWidth;

            for (int x = 0; x < width; x++)
            {
                int uvIdx = x / 2;
                byte yVal = yBuffer[yOffset + x];
                byte uVal = uBuffer[uvOffset + uvIdx];
                byte vVal = vBuffer[uvOffset + uvIdx];

                buffer[bufOffset + x * 4] = YuvToR(yVal, vVal);
                buffer[bufOffset + x * 4 + 1] = YuvToG(yVal, uVal, vVal);
                buffer[bufOffset + x * 4 + 2] = YuvToB(yVal, uVal);
                buffer[bufOffset + x * 4 + 3] = 255;
            }
        }
    }

    /// <summary>
    /// Converts RGB image data to YUV format.
    /// </summary>
    public static (byte[] y, byte[] u, byte[] v) ConvertImageToYuv(byte[] imageData, int width, int height, int bpp)
    {
        int mbWidth = (width + 15) / 16;
        int mbHeight = (height + 15) / 16;
        int ySize = 16 * mbWidth * 16 * mbHeight;
        int lumaWidth = 16 * mbWidth;
        int chromaWidth = 8 * mbWidth;
        int chromaSize = 8 * mbWidth * 8 * mbHeight;

        byte[] yBytes = new byte[ySize];
        byte[] uBytes = new byte[chromaSize];
        byte[] vBytes = new byte[chromaSize];

        // Process 2 rows at a time for chroma averaging
        for (int row = 0; row < height - 1; row += 2)
        {
            int imageRowOffset1 = row * width * bpp;
            int imageRowOffset2 = (row + 1) * width * bpp;
            int yRowOffset1 = row * lumaWidth;
            int yRowOffset2 = (row + 1) * lumaWidth;
            int uvRowOffset = (row / 2) * chromaWidth;

            for (int col = 0; col < width - 1; col += 2)
            {
                int rgb1Offset = imageRowOffset1 + col * bpp;
                int rgb2Offset = imageRowOffset1 + (col + 1) * bpp;
                int rgb3Offset = imageRowOffset2 + col * bpp;
                int rgb4Offset = imageRowOffset2 + (col + 1) * bpp;

                yBytes[yRowOffset1 + col] = RgbToY(imageData, rgb1Offset);
                yBytes[yRowOffset1 + col + 1] = RgbToY(imageData, rgb2Offset);
                yBytes[yRowOffset2 + col] = RgbToY(imageData, rgb3Offset);
                yBytes[yRowOffset2 + col + 1] = RgbToY(imageData, rgb4Offset);

                int uvIdx = uvRowOffset + col / 2;
                uBytes[uvIdx] = RgbToUAvg(imageData, rgb1Offset, rgb2Offset, rgb3Offset, rgb4Offset);
                vBytes[uvIdx] = RgbToVAvg(imageData, rgb1Offset, rgb2Offset, rgb3Offset, rgb4Offset);
            }
        }

        return (yBytes, uBytes, vBytes);
    }

    private static byte RgbToY(byte[] rgb, int offset)
    {
        int luma = 16839 * rgb[offset] + 33059 * rgb[offset + 1] + 6420 * rgb[offset + 2];
        return (byte)((luma + YuvHalf + (16 << YuvFix)) >> YuvFix);
    }

    private static byte RgbToUAvg(byte[] rgb, int o1, int o2, int o3, int o4)
    {
        int u1 = RgbToURaw(rgb, o1);
        int u2 = RgbToURaw(rgb, o2);
        int u3 = RgbToURaw(rgb, o3);
        int u4 = RgbToURaw(rgb, o4);
        return (byte)((u1 + u2 + u3 + u4) >> (YuvFix + 2));
    }

    private static byte RgbToVAvg(byte[] rgb, int o1, int o2, int o3, int o4)
    {
        int v1 = RgbToVRaw(rgb, o1);
        int v2 = RgbToVRaw(rgb, o2);
        int v3 = RgbToVRaw(rgb, o3);
        int v4 = RgbToVRaw(rgb, o4);
        return (byte)((v1 + v2 + v3 + v4) >> (YuvFix + 2));
    }

    private static int RgbToURaw(byte[] rgb, int offset)
    {
        return -9719 * rgb[offset] - 19081 * rgb[offset + 1] + 28800 * rgb[offset + 2] + (128 << YuvFix);
    }

    private static int RgbToVRaw(byte[] rgb, int offset)
    {
        return 28800 * rgb[offset] - 24116 * rgb[offset + 1] - 4684 * rgb[offset + 2] + (128 << YuvFix);
    }
}
