using System;
using System.IO;
using System.Text;

namespace TinyImage.Codecs.Pnm;

/// <summary>
/// Encodes images to Netpbm (PBM/PGM/PPM) format.
/// Supports all format variants: P1-P6 (ASCII and binary).
/// </summary>
internal sealed class PnmEncoder
{
    private readonly Stream _stream;
    private readonly int _width;
    private readonly int _height;
    private readonly byte[] _pixels;
    private readonly PnmFormat _format;

    /// <summary>
    /// Maximum line width for ASCII formats (for readability).
    /// </summary>
    private const int MaxLineWidth = 70;

    public PnmEncoder(Stream stream, int width, int height, byte[] pixels, PnmFormat format)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _pixels = pixels ?? throw new ArgumentNullException(nameof(pixels));

        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive.");
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive.");
        if (pixels.Length != width * height * 4)
            throw new ArgumentException("Pixel data size does not match dimensions.", nameof(pixels));

        _width = width;
        _height = height;
        _format = format;
    }

    /// <summary>
    /// Encodes the image to the output stream.
    /// </summary>
    public void Encode()
    {
        WriteHeader();

        switch (_format)
        {
            case PnmFormat.P1:
                EncodeP1();
                break;
            case PnmFormat.P2:
                EncodeP2();
                break;
            case PnmFormat.P3:
                EncodeP3();
                break;
            case PnmFormat.P4:
                EncodeP4();
                break;
            case PnmFormat.P5:
                EncodeP5();
                break;
            case PnmFormat.P6:
                EncodeP6();
                break;
            default:
                throw new NotSupportedException($"Unsupported PNM format: {_format}");
        }
    }

    #region Header Writing

    private void WriteHeader()
    {
        var sb = new StringBuilder();

        // Magic number
        sb.Append(_format.GetMagicNumber());
        sb.Append('\n');

        // Dimensions
        sb.Append(_width);
        sb.Append(' ');
        sb.Append(_height);
        sb.Append('\n');

        // Max value (not for PBM)
        if (!_format.IsBitmap())
        {
            sb.Append("255\n");
        }

        byte[] header = Encoding.ASCII.GetBytes(sb.ToString());
        _stream.Write(header, 0, header.Length);
    }

    #endregion

    #region ASCII Format Encoding

    private void EncodeP1()
    {
        // PBM ASCII: 0 = white, 1 = black
        var sb = new StringBuilder();
        int lineLength = 0;

        for (int i = 0; i < _width * _height; i++)
        {
            byte gray = GetGrayscale(i);
            char bit = gray >= 128 ? '0' : '1'; // >= 128 is white (0), else black (1)

            if (lineLength > 0)
            {
                if (lineLength >= MaxLineWidth)
                {
                    sb.Append('\n');
                    lineLength = 0;
                }
                else
                {
                    sb.Append(' ');
                    lineLength++;
                }
            }

            sb.Append(bit);
            lineLength++;

            // Flush periodically to avoid memory buildup
            if (sb.Length > 8192)
            {
                FlushString(sb);
            }
        }

        sb.Append('\n');
        FlushString(sb);
    }

    private void EncodeP2()
    {
        // PGM ASCII: grayscale values as decimal integers
        var sb = new StringBuilder();
        int lineLength = 0;

        for (int i = 0; i < _width * _height; i++)
        {
            byte gray = GetGrayscale(i);
            string value = gray.ToString();

            if (lineLength > 0)
            {
                if (lineLength + 1 + value.Length > MaxLineWidth)
                {
                    sb.Append('\n');
                    lineLength = 0;
                }
                else
                {
                    sb.Append(' ');
                    lineLength++;
                }
            }

            sb.Append(value);
            lineLength += value.Length;

            if (sb.Length > 8192)
            {
                FlushString(sb);
            }
        }

        sb.Append('\n');
        FlushString(sb);
    }

    private void EncodeP3()
    {
        // PPM ASCII: RGB triplets as decimal integers
        var sb = new StringBuilder();
        int lineLength = 0;

        for (int i = 0; i < _width * _height; i++)
        {
            int offset = i * 4;
            byte r = _pixels[offset];
            byte g = _pixels[offset + 1];
            byte b = _pixels[offset + 2];

            string triplet = $"{r} {g} {b}";

            if (lineLength > 0)
            {
                if (lineLength + 2 + triplet.Length > MaxLineWidth)
                {
                    sb.Append('\n');
                    lineLength = 0;
                }
                else
                {
                    sb.Append("  ");
                    lineLength += 2;
                }
            }

            sb.Append(triplet);
            lineLength += triplet.Length;

            if (sb.Length > 8192)
            {
                FlushString(sb);
            }
        }

        sb.Append('\n');
        FlushString(sb);
    }

    private void FlushString(StringBuilder sb)
    {
        if (sb.Length > 0)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(sb.ToString());
            _stream.Write(bytes, 0, bytes.Length);
            sb.Clear();
        }
    }

    #endregion

    #region Binary Format Encoding

    private void EncodeP4()
    {
        // PBM Binary: bits packed into bytes, MSB first
        // Each row is padded to byte boundary
        int bytesPerRow = (_width + 7) / 8;
        byte[] rowBuffer = new byte[bytesPerRow];

        for (int y = 0; y < _height; y++)
        {
            // Clear row buffer
            Array.Clear(rowBuffer, 0, bytesPerRow);

            for (int x = 0; x < _width; x++)
            {
                int pixelIndex = y * _width + x;
                byte gray = GetGrayscale(pixelIndex);

                // PBM: 0 = white, 1 = black
                // gray >= 128 -> white (bit = 0), else black (bit = 1)
                if (gray < 128)
                {
                    int byteIndex = x / 8;
                    int bitIndex = 7 - (x % 8); // MSB first
                    rowBuffer[byteIndex] |= (byte)(1 << bitIndex);
                }
            }

            _stream.Write(rowBuffer, 0, bytesPerRow);
        }
    }

    private void EncodeP5()
    {
        // PGM Binary: 1 byte per sample (maxval = 255)
        byte[] rowBuffer = new byte[_width];

        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                int pixelIndex = y * _width + x;
                rowBuffer[x] = GetGrayscale(pixelIndex);
            }

            _stream.Write(rowBuffer, 0, _width);
        }
    }

    private void EncodeP6()
    {
        // PPM Binary: 3 bytes per pixel (RGB, maxval = 255)
        byte[] rowBuffer = new byte[_width * 3];

        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                int pixelIndex = y * _width + x;
                int pixelOffset = pixelIndex * 4;
                int bufferOffset = x * 3;

                rowBuffer[bufferOffset] = _pixels[pixelOffset];     // R
                rowBuffer[bufferOffset + 1] = _pixels[pixelOffset + 1]; // G
                rowBuffer[bufferOffset + 2] = _pixels[pixelOffset + 2]; // B
            }

            _stream.Write(rowBuffer, 0, rowBuffer.Length);
        }
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Gets the grayscale value for a pixel using luminance formula.
    /// Y = 0.299*R + 0.587*G + 0.114*B
    /// </summary>
    private byte GetGrayscale(int pixelIndex)
    {
        int offset = pixelIndex * 4;
        int r = _pixels[offset];
        int g = _pixels[offset + 1];
        int b = _pixels[offset + 2];

        // Use integer arithmetic for better performance
        // Multiply by 1000 and divide to maintain precision
        int gray = (299 * r + 587 * g + 114 * b + 500) / 1000;
        return (byte)gray;
    }

    /// <summary>
    /// Analyzes the image to determine the best default format.
    /// </summary>
    public static PnmFormat DetermineOptimalFormat(byte[] pixels, int width, int height)
    {
        bool isGrayscale = true;
        bool isBinary = true;

        for (int i = 0; i < width * height; i++)
        {
            int offset = i * 4;
            byte r = pixels[offset];
            byte g = pixels[offset + 1];
            byte b = pixels[offset + 2];

            // Check if grayscale (R == G == B)
            if (r != g || g != b)
            {
                isGrayscale = false;
                isBinary = false;
                break;
            }

            // Check if binary (only 0 or 255)
            if (r != 0 && r != 255)
            {
                isBinary = false;
            }
        }

        // Return optimal binary format
        if (isBinary)
            return PnmFormat.P4; // Binary PBM
        if (isGrayscale)
            return PnmFormat.P5; // Binary PGM
        return PnmFormat.P6; // Binary PPM
    }

    #endregion
}
