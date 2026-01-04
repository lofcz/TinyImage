using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace TinyImage.Resize;

/// <summary>
/// Image resizing algorithms.
/// </summary>
internal static class Resizer
{
    /// <summary>
    /// Resizes an image using the specified interpolation mode.
    /// All frames are resized for animated images.
    /// </summary>
    /// <param name="source">The source image.</param>
    /// <param name="newWidth">The new width.</param>
    /// <param name="newHeight">The new height.</param>
    /// <param name="mode">The interpolation mode.</param>
    /// <returns>The resized image with all frames.</returns>
    public static Image Resize(Image source, int newWidth, int newHeight, ResizeMode mode)
    {
        // Resize all frames
        var resizedFrames = new List<ImageFrame>(source.Frames.Count);
        
        foreach (var srcFrame in source.Frames)
        {
            var resizedFrame = ResizeFrame(srcFrame, newWidth, newHeight, mode);
            resizedFrames.Add(resizedFrame);
        }

        return new Image(resizedFrames, source.HasAlpha, source.LoopCount);
    }

    /// <summary>
    /// Resizes a single frame using the specified interpolation mode.
    /// </summary>
    private static ImageFrame ResizeFrame(ImageFrame source, int newWidth, int newHeight, ResizeMode mode)
    {
        var destBuffer = mode switch
        {
            ResizeMode.NearestNeighbor => ResizeBufferNearestNeighbor(source.Buffer, newWidth, newHeight),
            ResizeMode.Bilinear => ResizeBufferBilinear(source.Buffer, newWidth, newHeight),
            ResizeMode.Bicubic => ResizeBufferBicubic(source.Buffer, newWidth, newHeight),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };

        return new ImageFrame(destBuffer)
        {
            Duration = source.Duration
        };
    }

    /// <summary>
    /// Nearest-neighbor interpolation. Fastest, but produces pixelated results.
    /// </summary>
    private static PixelBuffer ResizeBufferNearestNeighbor(PixelBuffer srcBuffer, int newWidth, int newHeight)
    {
        var destBuffer = new PixelBuffer(newWidth, newHeight);

        int srcWidth = srcBuffer.Width;
        int srcHeight = srcBuffer.Height;

        double xRatio = (double)srcWidth / newWidth;
        double yRatio = (double)srcHeight / newHeight;

        for (int y = 0; y < newHeight; y++)
        {
            int srcY = (int)(y * yRatio);
            if (srcY >= srcHeight) srcY = srcHeight - 1;

            for (int x = 0; x < newWidth; x++)
            {
                int srcX = (int)(x * xRatio);
                if (srcX >= srcWidth) srcX = srcWidth - 1;

                destBuffer.SetPixel(x, y, srcBuffer.GetPixel(srcX, srcY));
            }
        }

        return destBuffer;
    }

    /// <summary>
    /// Bilinear interpolation. Good balance between quality and speed.
    /// </summary>
    private static PixelBuffer ResizeBufferBilinear(PixelBuffer srcBuffer, int newWidth, int newHeight)
    {
        var destBuffer = new PixelBuffer(newWidth, newHeight);

        int srcWidth = srcBuffer.Width;
        int srcHeight = srcBuffer.Height;

        double xRatio = (double)(srcWidth - 1) / Math.Max(newWidth - 1, 1);
        double yRatio = (double)(srcHeight - 1) / Math.Max(newHeight - 1, 1);

        for (int y = 0; y < newHeight; y++)
        {
            double srcY = y * yRatio;
            int y0 = (int)srcY;
            int y1 = Math.Min(y0 + 1, srcHeight - 1);
            double yFrac = srcY - y0;

            for (int x = 0; x < newWidth; x++)
            {
                double srcX = x * xRatio;
                int x0 = (int)srcX;
                int x1 = Math.Min(x0 + 1, srcWidth - 1);
                double xFrac = srcX - x0;

                // Get the four nearest pixels
                var p00 = srcBuffer.GetPixel(x0, y0);
                var p10 = srcBuffer.GetPixel(x1, y0);
                var p01 = srcBuffer.GetPixel(x0, y1);
                var p11 = srcBuffer.GetPixel(x1, y1);

                // Interpolate
                var result = BilinearInterpolate(p00, p10, p01, p11, xFrac, yFrac);
                destBuffer.SetPixel(x, y, result);
            }
        }

        return destBuffer;
    }

    /// <summary>
    /// Bicubic interpolation. Higher quality results, slower than bilinear.
    /// </summary>
    private static PixelBuffer ResizeBufferBicubic(PixelBuffer srcBuffer, int newWidth, int newHeight)
    {
        var destBuffer = new PixelBuffer(newWidth, newHeight);

        int srcWidth = srcBuffer.Width;
        int srcHeight = srcBuffer.Height;

        double xRatio = (double)(srcWidth - 1) / Math.Max(newWidth - 1, 1);
        double yRatio = (double)(srcHeight - 1) / Math.Max(newHeight - 1, 1);

        for (int y = 0; y < newHeight; y++)
        {
            double srcY = y * yRatio;
            int y1 = (int)srcY;
            double yFrac = srcY - y1;

            for (int x = 0; x < newWidth; x++)
            {
                double srcX = x * xRatio;
                int x1 = (int)srcX;
                double xFrac = srcX - x1;

                // Get the 4x4 grid of pixels
                var result = BicubicInterpolate(srcBuffer, srcWidth, srcHeight, x1, y1, xFrac, yFrac);
                destBuffer.SetPixel(x, y, result);
            }
        }

        return destBuffer;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Rgba32 BilinearInterpolate(Rgba32 p00, Rgba32 p10, Rgba32 p01, Rgba32 p11, double xFrac, double yFrac)
    {
        double x0Weight = 1.0 - xFrac;
        double y0Weight = 1.0 - yFrac;

        double w00 = x0Weight * y0Weight;
        double w10 = xFrac * y0Weight;
        double w01 = x0Weight * yFrac;
        double w11 = xFrac * yFrac;

        byte r = MathExt.ClampToByte(p00.R * w00 + p10.R * w10 + p01.R * w01 + p11.R * w11);
        byte g = MathExt.ClampToByte(p00.G * w00 + p10.G * w10 + p01.G * w01 + p11.G * w11);
        byte b = MathExt.ClampToByte(p00.B * w00 + p10.B * w10 + p01.B * w01 + p11.B * w11);
        byte a = MathExt.ClampToByte(p00.A * w00 + p10.A * w10 + p01.A * w01 + p11.A * w11);

        return new Rgba32(r, g, b, a);
    }

    private static Rgba32 BicubicInterpolate(PixelBuffer buffer, int width, int height, int x1, int y1, double xFrac, double yFrac)
    {
        double rSum = 0, gSum = 0, bSum = 0, aSum = 0;

        for (int j = -1; j <= 2; j++)
        {
            int y = MathExt.Clamp(y1 + j, 0, height - 1);
            double wy = CubicWeight(j - yFrac);

            for (int i = -1; i <= 2; i++)
            {
                int x = MathExt.Clamp(x1 + i, 0, width - 1);
                double wx = CubicWeight(i - xFrac);
                double w = wx * wy;

                var pixel = buffer.GetPixel(x, y);
                rSum += pixel.R * w;
                gSum += pixel.G * w;
                bSum += pixel.B * w;
                aSum += pixel.A * w;
            }
        }

        return new Rgba32(
            MathExt.ClampToByte(rSum),
            MathExt.ClampToByte(gSum),
            MathExt.ClampToByte(bSum),
            MathExt.ClampToByte(aSum)
        );
    }

    /// <summary>
    /// Cubic interpolation weight function (Catmull-Rom spline).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CubicWeight(double x)
    {
        x = Math.Abs(x);
        
        if (x < 1.0)
        {
            return (1.5 * x - 2.5) * x * x + 1.0;
        }
        else if (x < 2.0)
        {
            return ((-0.5 * x + 2.5) * x - 4.0) * x + 2.0;
        }
        
        return 0.0;
    }
}
