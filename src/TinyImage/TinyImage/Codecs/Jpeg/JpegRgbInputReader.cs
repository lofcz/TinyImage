using System;
using System.Runtime.CompilerServices;

namespace TinyImage.Codecs.Jpeg;

/// <summary>
/// Input reader that reads RGB pixel data and converts to YCbCr for JPEG encoding.
/// </summary>
internal sealed class JpegRgbInputReader : JpegBlockInputReader
{
    private readonly byte[] _buffer;
    private readonly int _width;
    private readonly int _height;

    public JpegRgbInputReader(byte[] rgbaBuffer, int width, int height)
    {
        _buffer = rgbaBuffer;
        _width = width;
        _height = height;
    }

    public override int Width => _width;
    public override int Height => _height;

    public override void ReadBlock(ref short blockRef, int componentIndex, int x, int y)
    {
        int w = _width;
        int h = _height;

        int blockWidth = Math.Min(8, w - x);
        int blockHeight = Math.Min(8, h - y);

        ref short destRef = ref blockRef;

        for (int j = 0; j < 8; j++)
        {
            int srcY = y + j;
            if (srcY >= h) srcY = h - 1; // Edge padding

            for (int i = 0; i < 8; i++)
            {
                int srcX = x + i;
                if (srcX >= w) srcX = w - 1; // Edge padding

                int srcOffset = (srcY * w + srcX) * 4;
                int destOffset = j * 8 + i;

                int r = _buffer[srcOffset];
                int g = _buffer[srcOffset + 1];
                int b = _buffer[srcOffset + 2];

                // RGB to YCbCr conversion (ITU-R BT.601)
                short value;
                switch (componentIndex)
                {
                    case 0: // Y
                        value = (short)(0.299 * r + 0.587 * g + 0.114 * b);
                        break;
                    case 1: // Cb
                        value = (short)(128 - 0.168736 * r - 0.331264 * g + 0.5 * b);
                        break;
                    case 2: // Cr
                        value = (short)(128 + 0.5 * r - 0.418688 * g - 0.081312 * b);
                        break;
                    default:
                        value = 0;
                        break;
                }

                Unsafe.Add(ref destRef, destOffset) = value;
            }
        }
    }
}
