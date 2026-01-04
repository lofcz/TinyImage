using System;
using System.Runtime.CompilerServices;

namespace TinyImage.Codecs.Jpeg;

/// <summary>
/// Output writer that writes JPEG blocks to an RGBA32 buffer with YCbCr to RGB conversion.
/// </summary>
internal sealed class JpegRgbOutputWriter : JpegBlockOutputWriter
{
    private readonly int _width;
    private readonly int _height;
    private readonly int _componentCount;
    private readonly byte[] _buffer;

    public JpegRgbOutputWriter(int width, int height, int componentCount)
    {
        if (componentCount != 1 && componentCount != 3 && componentCount != 4)
            throw new ArgumentException("Component count must be 1, 3, or 4.", nameof(componentCount));

        _width = width;
        _height = height;
        _componentCount = componentCount;
        _buffer = new byte[width * height * 4];
    }

    public byte[] GetBuffer() => _buffer;
    
    public bool IsGrayscale => _componentCount == 1;

    public override void WriteBlock(ref short blockRef, int componentIndex, int x, int y)
    {
        int w = _width;
        int h = _height;

        if (x >= w || y >= h)
            return;

        int blockWidth = Math.Min(8, w - x);
        int blockHeight = Math.Min(8, h - y);

        ref short sourceRef = ref blockRef;

        if (_componentCount == 1)
        {
            // Grayscale
            for (int j = 0; j < blockHeight; j++)
            {
                int destOffset = ((y + j) * w + x) * 4;
                int srcOffset = j * 8;

                for (int i = 0; i < blockWidth; i++)
                {
                    byte gray = ClampToByte(Unsafe.Add(ref sourceRef, srcOffset + i));
                    _buffer[destOffset] = gray;
                    _buffer[destOffset + 1] = gray;
                    _buffer[destOffset + 2] = gray;
                    _buffer[destOffset + 3] = 255;
                    destOffset += 4;
                }
            }
        }
        else
        {
            // YCbCr or CMYK - store component values directly
            // The actual conversion will be done in a post-processing step
            int componentOffset = componentIndex;

            for (int j = 0; j < blockHeight; j++)
            {
                int destOffset = ((y + j) * w + x) * 4 + componentOffset;
                int srcOffset = j * 8;

                for (int i = 0; i < blockWidth; i++)
                {
                    _buffer[destOffset] = ClampToByte(Unsafe.Add(ref sourceRef, srcOffset + i));
                    destOffset += 4;
                }
            }
        }
    }

    /// <summary>
    /// Converts the YCbCr buffer to RGB in-place.
    /// </summary>
    public void ConvertYCbCrToRgb()
    {
        if (_componentCount != 3)
            return;

        int pixelCount = _width * _height;
        for (int i = 0; i < pixelCount; i++)
        {
            int offset = i * 4;

            int y = _buffer[offset];      // Y stored in R
            int cb = _buffer[offset + 1]; // Cb stored in G
            int cr = _buffer[offset + 2]; // Cr stored in B

            // YCbCr to RGB conversion (ITU-R BT.601)
            int r = y + (int)(1.402 * (cr - 128));
            int g = y - (int)(0.344136 * (cb - 128)) - (int)(0.714136 * (cr - 128));
            int b = y + (int)(1.772 * (cb - 128));

            _buffer[offset] = ClampToByte(r);
            _buffer[offset + 1] = ClampToByte(g);
            _buffer[offset + 2] = ClampToByte(b);
            _buffer[offset + 3] = 255;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ClampToByte(int value)
    {
        if (value < 0) return 0;
        if (value > 255) return 255;
        return (byte)value;
    }
}
