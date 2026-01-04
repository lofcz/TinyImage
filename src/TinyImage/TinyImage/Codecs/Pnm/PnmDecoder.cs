using System;
using System.IO;
using System.Text;

namespace TinyImage.Codecs.Pnm;

/// <summary>
/// Decodes Netpbm (PBM/PGM/PPM) images from a stream.
/// Supports all format variants: P1-P6 (ASCII and binary).
/// </summary>
internal sealed class PnmDecoder
{
    private readonly Stream _stream;
    private readonly byte[] _buffer;
    private int _bufferPos;
    private int _bufferLength;

    // Parsed header
    private PnmHeader _header;

    // Output
    private int _width;
    private int _height;
    private byte[]? _pixels;

    private const int BufferSize = 8192;

    public PnmDecoder(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _buffer = new byte[BufferSize];
        _bufferPos = 0;
        _bufferLength = 0;
    }

    /// <summary>
    /// Decodes the PNM image and returns the RGBA pixel data.
    /// </summary>
    public (int width, int height, byte[] pixels, bool hasAlpha) Decode()
    {
        _header = ReadHeader();

        if (!_header.IsValid())
            throw new InvalidOperationException("Invalid PNM header.");

        _width = _header.Width;
        _height = _header.Height;
        _pixels = new byte[_width * _height * 4];

        // Seek to pixel data start
        _stream.Position = _header.PixelDataOffset;
        _bufferPos = 0;
        _bufferLength = 0;

        // Decode based on format
        switch (_header.Format)
        {
            case PnmFormat.P1:
                DecodeP1();
                break;
            case PnmFormat.P2:
                DecodeP2();
                break;
            case PnmFormat.P3:
                DecodeP3();
                break;
            case PnmFormat.P4:
                DecodeP4();
                break;
            case PnmFormat.P5:
                DecodeP5();
                break;
            case PnmFormat.P6:
                DecodeP6();
                break;
            default:
                throw new NotSupportedException($"Unsupported PNM format: {_header.Format}");
        }

        return (_width, _height, _pixels, false);
    }

    #region Header Parsing

    private PnmHeader ReadHeader()
    {
        // Read enough bytes for header parsing
        FillBuffer();

        int pos = 0;
        StringBuilder? comment = null;

        // Read magic number (P1-P6)
        string magic = ReadToken(ref pos);
        if (!PnmFormatExtensions.TryParse(magic, out var format))
            throw new InvalidOperationException($"Invalid PNM magic number: {magic}");

        // Skip comments and read width
        SkipWhitespaceAndComments(ref pos, ref comment);
        string widthStr = ReadToken(ref pos);
        if (!int.TryParse(widthStr, out int width) || width <= 0)
            throw new InvalidOperationException($"Invalid width: {widthStr}");

        // Skip comments and read height
        SkipWhitespaceAndComments(ref pos, ref comment);
        string heightStr = ReadToken(ref pos);
        if (!int.TryParse(heightStr, out int height) || height <= 0)
            throw new InvalidOperationException($"Invalid height: {heightStr}");

        int maxValue;
        if (format.IsBitmap())
        {
            // PBM has no maxval field
            maxValue = 1;
        }
        else
        {
            // Read maxval for PGM/PPM
            SkipWhitespaceAndComments(ref pos, ref comment);
            string maxValStr = ReadToken(ref pos);
            if (!int.TryParse(maxValStr, out maxValue) || maxValue < 1 || maxValue > 65535)
                throw new InvalidOperationException($"Invalid maxval: {maxValStr}");
        }

        // After the last header value, there must be exactly one whitespace character
        // before the pixel data begins
        if (pos < _bufferLength && IsWhitespace(_buffer[pos]))
            pos++;

        return new PnmHeader(format, width, height, maxValue, pos, comment?.ToString());
    }

    private string ReadToken(ref int pos)
    {
        // Skip leading whitespace
        while (pos < _bufferLength && IsWhitespace(_buffer[pos]))
            pos++;

        int start = pos;
        while (pos < _bufferLength && !IsWhitespace(_buffer[pos]) && _buffer[pos] != '#')
            pos++;

        if (pos == start)
            throw new InvalidOperationException("Unexpected end of header.");

        return Encoding.ASCII.GetString(_buffer, start, pos - start);
    }

    private void SkipWhitespaceAndComments(ref int pos, ref StringBuilder? comment)
    {
        while (pos < _bufferLength)
        {
            byte b = _buffer[pos];

            if (IsWhitespace(b))
            {
                pos++;
            }
            else if (b == '#')
            {
                // Read comment until end of line
                pos++; // skip #
                int commentStart = pos;
                while (pos < _bufferLength && _buffer[pos] != '\n' && _buffer[pos] != '\r')
                    pos++;

                if (comment == null)
                    comment = new StringBuilder();
                else
                    comment.AppendLine();

                comment.Append(Encoding.ASCII.GetString(_buffer, commentStart, pos - commentStart).Trim());

                // Skip newline
                if (pos < _bufferLength && (_buffer[pos] == '\r' || _buffer[pos] == '\n'))
                    pos++;
                if (pos < _bufferLength && _buffer[pos] == '\n')
                    pos++;
            }
            else
            {
                break;
            }
        }
    }

    private static bool IsWhitespace(byte b) => b == ' ' || b == '\t' || b == '\r' || b == '\n';

    #endregion

    #region ASCII Format Decoding

    private void DecodeP1()
    {
        // PBM ASCII: 0 = white, 1 = black
        int pixelIndex = 0;
        int totalPixels = _width * _height;

        while (pixelIndex < totalPixels)
        {
            int b = ReadByteBuffered();
            if (b < 0)
                break;

            // Skip whitespace and comments
            if (IsWhitespace((byte)b))
                continue;

            if (b == '#')
            {
                // Skip comment line
                SkipToEndOfLine();
                continue;
            }

            if (b == '0')
            {
                // White pixel
                SetPixelGray(pixelIndex++, 255);
            }
            else if (b == '1')
            {
                // Black pixel
                SetPixelGray(pixelIndex++, 0);
            }
            // Ignore other characters
        }
    }

    private void DecodeP2()
    {
        // PGM ASCII: grayscale values as decimal integers
        int pixelIndex = 0;
        int totalPixels = _width * _height;
        int maxValue = _header.MaxValue;

        while (pixelIndex < totalPixels)
        {
            int value = ReadAsciiInteger();
            if (value < 0)
                break;

            // Normalize to 0-255
            byte gray = NormalizeValue(value, maxValue);
            SetPixelGray(pixelIndex++, gray);
        }
    }

    private void DecodeP3()
    {
        // PPM ASCII: RGB triplets as decimal integers
        int pixelIndex = 0;
        int totalPixels = _width * _height;
        int maxValue = _header.MaxValue;

        while (pixelIndex < totalPixels)
        {
            int r = ReadAsciiInteger();
            int g = ReadAsciiInteger();
            int b = ReadAsciiInteger();

            if (r < 0 || g < 0 || b < 0)
                break;

            // Normalize to 0-255
            SetPixelRgb(pixelIndex++,
                NormalizeValue(r, maxValue),
                NormalizeValue(g, maxValue),
                NormalizeValue(b, maxValue));
        }
    }

    private int ReadAsciiInteger()
    {
        // Skip whitespace and comments
        int b;
        while (true)
        {
            b = ReadByteBuffered();
            if (b < 0)
                return -1;

            if (b == '#')
            {
                SkipToEndOfLine();
                continue;
            }

            if (!IsWhitespace((byte)b))
                break;
        }

        // Read digits
        if (b < '0' || b > '9')
            return -1;

        int value = b - '0';
        while (true)
        {
            b = PeekByteBuffered();
            if (b < 0 || b < '0' || b > '9')
                break;

            ReadByteBuffered(); // consume the peeked byte
            value = value * 10 + (b - '0');
        }

        return value;
    }

    private void SkipToEndOfLine()
    {
        int b;
        while ((b = ReadByteBuffered()) >= 0)
        {
            if (b == '\n' || b == '\r')
                break;
        }
    }

    #endregion

    #region Binary Format Decoding

    private void DecodeP4()
    {
        // PBM Binary: bits packed into bytes, MSB first
        // Each row is padded to byte boundary
        int bytesPerRow = (_width + 7) / 8;
        byte[] rowBuffer = new byte[bytesPerRow];

        for (int y = 0; y < _height; y++)
        {
            int bytesRead = _stream.Read(rowBuffer, 0, bytesPerRow);
            if (bytesRead < bytesPerRow)
                throw new InvalidOperationException("Unexpected end of pixel data.");

            for (int x = 0; x < _width; x++)
            {
                int byteIndex = x / 8;
                int bitIndex = 7 - (x % 8); // MSB first
                int bit = (rowBuffer[byteIndex] >> bitIndex) & 1;

                // PBM: 0 = white, 1 = black
                int pixelIndex = y * _width + x;
                SetPixelGray(pixelIndex, bit == 0 ? (byte)255 : (byte)0);
            }
        }
    }

    private void DecodeP5()
    {
        // PGM Binary: 1 or 2 bytes per sample
        int maxValue = _header.MaxValue;
        bool is16Bit = maxValue > 255;
        int bytesPerPixel = is16Bit ? 2 : 1;
        int totalBytes = _width * _height * bytesPerPixel;
        byte[] data = new byte[totalBytes];

        int bytesRead = _stream.Read(data, 0, totalBytes);
        if (bytesRead < totalBytes)
            throw new InvalidOperationException("Unexpected end of pixel data.");

        int dataIndex = 0;
        for (int i = 0; i < _width * _height; i++)
        {
            int value;
            if (is16Bit)
            {
                // Big-endian 16-bit
                value = (data[dataIndex] << 8) | data[dataIndex + 1];
                dataIndex += 2;
            }
            else
            {
                value = data[dataIndex++];
            }

            byte gray = NormalizeValue(value, maxValue);
            SetPixelGray(i, gray);
        }
    }

    private void DecodeP6()
    {
        // PPM Binary: 3 or 6 bytes per pixel (RGB)
        int maxValue = _header.MaxValue;
        bool is16Bit = maxValue > 255;
        int bytesPerPixel = is16Bit ? 6 : 3;
        int totalBytes = _width * _height * bytesPerPixel;
        byte[] data = new byte[totalBytes];

        int bytesRead = _stream.Read(data, 0, totalBytes);
        if (bytesRead < totalBytes)
            throw new InvalidOperationException("Unexpected end of pixel data.");

        int dataIndex = 0;
        for (int i = 0; i < _width * _height; i++)
        {
            int r, g, b;
            if (is16Bit)
            {
                // Big-endian 16-bit per channel
                r = (data[dataIndex] << 8) | data[dataIndex + 1];
                g = (data[dataIndex + 2] << 8) | data[dataIndex + 3];
                b = (data[dataIndex + 4] << 8) | data[dataIndex + 5];
                dataIndex += 6;
            }
            else
            {
                r = data[dataIndex++];
                g = data[dataIndex++];
                b = data[dataIndex++];
            }

            SetPixelRgb(i,
                NormalizeValue(r, maxValue),
                NormalizeValue(g, maxValue),
                NormalizeValue(b, maxValue));
        }
    }

    #endregion

    #region Helpers

    private void FillBuffer()
    {
        _bufferLength = _stream.Read(_buffer, 0, BufferSize);
        _bufferPos = 0;
    }

    private int ReadByteBuffered()
    {
        if (_bufferPos >= _bufferLength)
        {
            FillBuffer();
            if (_bufferLength == 0)
                return -1;
        }
        return _buffer[_bufferPos++];
    }

    private int PeekByteBuffered()
    {
        if (_bufferPos >= _bufferLength)
        {
            FillBuffer();
            if (_bufferLength == 0)
                return -1;
        }
        return _buffer[_bufferPos];
    }

    private static byte NormalizeValue(int value, int maxValue)
    {
        if (maxValue == 255)
            return (byte)value;
        if (maxValue == 1)
            return value == 0 ? (byte)0 : (byte)255;

        // Scale to 0-255 range
        return (byte)((value * 255 + maxValue / 2) / maxValue);
    }

    private void SetPixelGray(int index, byte gray)
    {
        int offset = index * 4;
        _pixels![offset] = gray;
        _pixels[offset + 1] = gray;
        _pixels[offset + 2] = gray;
        _pixels[offset + 3] = 255;
    }

    private void SetPixelRgb(int index, byte r, byte g, byte b)
    {
        int offset = index * 4;
        _pixels![offset] = r;
        _pixels[offset + 1] = g;
        _pixels[offset + 2] = b;
        _pixels[offset + 3] = 255;
    }

    #endregion
}
