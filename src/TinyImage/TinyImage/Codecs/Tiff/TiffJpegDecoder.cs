using System;
using System.Buffers;
using TinyImage.Codecs.Jpeg;

namespace TinyImage.Codecs.Tiff;

/// <summary>
/// Wrapper for decoding JPEG-compressed data within TIFF files.
/// Based on image-tiff handling of JPEGTables and ModernJPEG compression.
/// </summary>
internal sealed class TiffJpegDecoder
{
    /// <summary>
    /// Clamps a value between min and max (netstandard2.0 compatible).
    /// </summary>
    private static int Clamp(int value, int min, int max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    private readonly byte[]? _jpegTables;

    /// <summary>
    /// Creates a new TIFF JPEG decoder.
    /// </summary>
    /// <param name="jpegTables">Optional JPEG tables from the JpegTables TIFF tag.</param>
    public TiffJpegDecoder(byte[]? jpegTables)
    {
        _jpegTables = jpegTables;
    }

    /// <summary>
    /// Decodes JPEG-compressed strip or tile data.
    /// </summary>
    /// <param name="compressedData">The JPEG-compressed data.</param>
    /// <param name="expectedWidth">Expected width of the decoded data.</param>
    /// <param name="expectedHeight">Expected height of the decoded data.</param>
    /// <returns>The decoded pixel data.</returns>
    public byte[] Decode(byte[] compressedData, int expectedWidth, int expectedHeight)
    {
        byte[] jpegStream;

        if (_jpegTables != null && _jpegTables.Length > 4)
        {
            // Combine JPEG tables with strip/tile data
            // Tables end with EOI marker (0xFF 0xD9) which should be removed
            // Strip data starts with SOI marker (0xFF 0xD8) which should be removed
            jpegStream = CombineTablesAndData(_jpegTables, compressedData);
        }
        else
        {
            // No tables, use data as-is (should be a complete JPEG stream)
            jpegStream = compressedData;
        }

        return DecodeJpegStream(jpegStream, expectedWidth, expectedHeight);
    }

    /// <summary>
    /// Combines JPEG tables with strip/tile data to form a valid JPEG stream.
    /// </summary>
    private static byte[] CombineTablesAndData(byte[] tables, byte[] data)
    {
        // JPEG tables structure: SOI + tables + EOI (we need to remove EOI)
        // Strip data structure: SOI + data (we need to remove SOI)
        
        // Find end of tables (skip EOI marker at end)
        int tablesEnd = tables.Length;
        if (tables.Length >= 2 && 
            tables[tables.Length - 2] == 0xFF && 
            tables[tables.Length - 1] == 0xD9)
        {
            tablesEnd = tables.Length - 2;
        }

        // Find start of data (skip SOI marker at start)
        int dataStart = 0;
        if (data.Length >= 2 && 
            data[0] == 0xFF && 
            data[1] == 0xD8)
        {
            dataStart = 2;
        }

        // Combine: tables[0..tablesEnd-1] + data[dataStart..end]
        var result = new byte[tablesEnd + (data.Length - dataStart)];
        Buffer.BlockCopy(tables, 0, result, 0, tablesEnd);
        Buffer.BlockCopy(data, dataStart, result, tablesEnd, data.Length - dataStart);

        return result;
    }

    /// <summary>
    /// Decodes a JPEG stream to raw pixel data.
    /// </summary>
    private static byte[] DecodeJpegStream(byte[] jpegStream, int expectedWidth, int expectedHeight)
    {
        var decoder = new JpegDecoder();
        decoder.SetInput(jpegStream);
        decoder.Identify();

        int width = decoder.Width;
        int height = decoder.Height;
        int components = decoder.NumberOfComponents;

        // Allocate output buffer (RGB or CMYK data)
        var outputBuffer = new byte[width * height * components];

        // Create output writer based on number of components
        JpegBlockOutputWriter outputWriter;
        if (components == 1)
        {
            outputWriter = new JpegGrayscaleOutputWriter(width, height, outputBuffer);
        }
        else if (components == 3)
        {
            outputWriter = new JpegYCbCrOutputWriter(width, height, outputBuffer);
        }
        else if (components == 4)
        {
            outputWriter = new JpegCmykOutputWriter(width, height, outputBuffer);
        }
        else
        {
            throw new TiffUnsupportedException($"JPEG with {components} components is not supported in TIFF.");
        }

        decoder.SetOutputWriter(outputWriter);
        decoder.Decode();

        return outputBuffer;
    }
}

/// <summary>
/// Output writer for grayscale JPEG decoding.
/// </summary>
internal sealed class JpegGrayscaleOutputWriter : JpegBlockOutputWriter
{
    private readonly int _width;
    private readonly int _height;
    private readonly byte[] _output;

    public JpegGrayscaleOutputWriter(int width, int height, byte[] output)
    {
        _width = width;
        _height = height;
        _output = output;
    }

    public override void WriteBlock(ref short blockRef, int componentIndex, int x, int y)
    {
        int blockWidth = Math.Min(8, _width - x);
        int blockHeight = Math.Min(8, _height - y);

        for (int dy = 0; dy < blockHeight; dy++)
        {
            for (int dx = 0; dx < blockWidth; dx++)
            {
                int px = x + dx;
                int py = y + dy;
                if (px >= _width || py >= _height) continue;

                int srcIdx = dy * 8 + dx;
                int value = System.Runtime.CompilerServices.Unsafe.Add(ref blockRef, srcIdx);
                    value = Clamp(value, 0, 255);

                _output[py * _width + px] = (byte)value;
            }
        }
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
}

/// <summary>
/// Output writer for YCbCr JPEG decoding (converts to RGB).
/// </summary>
internal sealed class JpegYCbCrOutputWriter : JpegBlockOutputWriter
{
    private readonly int _width;
    private readonly int _height;
    private readonly byte[] _output;
    private readonly short[] _y;
    private readonly short[] _cb;
    private readonly short[] _cr;

    public JpegYCbCrOutputWriter(int width, int height, byte[] output)
    {
        _width = width;
        _height = height;
        _output = output;
        _y = new short[width * height];
        _cb = new short[width * height];
        _cr = new short[width * height];
    }

    public override void WriteBlock(ref short blockRef, int componentIndex, int x, int y)
    {
        short[] target = componentIndex switch
        {
            0 => _y,
            1 => _cb,
            2 => _cr,
            _ => _y
        };

        int blockWidth = Math.Min(8, _width - x);
        int blockHeight = Math.Min(8, _height - y);

        for (int dy = 0; dy < blockHeight; dy++)
        {
            for (int dx = 0; dx < blockWidth; dx++)
            {
                int px = x + dx;
                int py = y + dy;
                if (px >= _width || py >= _height) continue;

                int srcIdx = dy * 8 + dx;
                short value = System.Runtime.CompilerServices.Unsafe.Add(ref blockRef, srcIdx);
                target[py * _width + px] = value;
            }
        }
    }

    public void ConvertToRgb()
    {
        for (int i = 0; i < _width * _height; i++)
        {
            int yVal = _y[i];
            int cb = _cb[i];
            int cr = _cr[i];

            // YCbCr to RGB conversion (ITU-R BT.601)
            int r = yVal + (int)(1.402 * (cr - 128));
            int g = yVal - (int)(0.344136 * (cb - 128)) - (int)(0.714136 * (cr - 128));
            int b = yVal + (int)(1.772 * (cb - 128));

            _output[i * 3] = (byte)Clamp(r, 0, 255);
            _output[i * 3 + 1] = (byte)Clamp(g, 0, 255);
            _output[i * 3 + 2] = (byte)Clamp(b, 0, 255);
        }
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
}

/// <summary>
/// Output writer for CMYK JPEG decoding.
/// </summary>
internal sealed class JpegCmykOutputWriter : JpegBlockOutputWriter
{
    private readonly int _width;
    private readonly int _height;
    private readonly byte[] _output;

    public JpegCmykOutputWriter(int width, int height, byte[] output)
    {
        _width = width;
        _height = height;
        _output = output;
    }

    public override void WriteBlock(ref short blockRef, int componentIndex, int x, int y)
    {
        int blockWidth = Math.Min(8, _width - x);
        int blockHeight = Math.Min(8, _height - y);

        for (int dy = 0; dy < blockHeight; dy++)
        {
            for (int dx = 0; dx < blockWidth; dx++)
            {
                int px = x + dx;
                int py = y + dy;
                if (px >= _width || py >= _height) continue;

                int srcIdx = dy * 8 + dx;
                int value = System.Runtime.CompilerServices.Unsafe.Add(ref blockRef, srcIdx);
                    value = Clamp(value, 0, 255);

                int outIdx = (py * _width + px) * 4 + componentIndex;
                if (outIdx < _output.Length)
                {
                    _output[outIdx] = (byte)value;
                }
            }
        }
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
}
