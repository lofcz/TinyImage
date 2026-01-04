using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.CompilerServices;

namespace TinyImage.Codecs.Qoi;

/// <summary>
/// Decodes QOI (Quite OK Image) format data into raw RGBA32 pixels.
/// </summary>
/// <remarks>
/// Based on QoiSharp by Eugene Antonov (MIT License) and the QOI specification
/// by Dominic Szablewski.
/// </remarks>
internal sealed class QoiDecoder
{
    private readonly Stream _stream;
    private readonly byte[] _buffer;
    private int _bufferPos;
    private int _bufferLength;

    // Decoded image properties
    private int _width;
    private int _height;
    private byte _channels;
    private QoiColorSpace _colorSpace;

    /// <summary>
    /// Gets the decoded image width.
    /// </summary>
    public int Width => _width;

    /// <summary>
    /// Gets the decoded image height.
    /// </summary>
    public int Height => _height;

    /// <summary>
    /// Gets the number of channels in the image (3 for RGB, 4 for RGBA).
    /// </summary>
    public byte Channels => _channels;

    /// <summary>
    /// Gets the color space of the image.
    /// </summary>
    public QoiColorSpace ColorSpace => _colorSpace;

    /// <summary>
    /// Creates a new QOI decoder for the specified stream.
    /// </summary>
    /// <param name="stream">The stream containing QOI data.</param>
    public QoiDecoder(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _buffer = new byte[8192];
        _bufferPos = 0;
        _bufferLength = 0;
    }

    /// <summary>
    /// Decodes the QOI data and returns the RGBA32 pixel data.
    /// </summary>
    /// <returns>Tuple containing width, height, RGBA32 pixel data, and whether alpha is used.</returns>
    public (int width, int height, byte[] pixels, bool hasAlpha) Decode()
    {
        ReadHeader();

        // Allocate output buffer (always RGBA32)
        byte[] pixels = new byte[_width * _height * 4];

        DecodePixels(pixels);

        // Check if any pixel has non-255 alpha (indicates meaningful alpha channel)
        bool hasAlpha = _channels == 4 && HasMeaningfulAlpha(pixels);

        return (_width, _height, pixels, hasAlpha);
    }

    private void ReadHeader()
    {
        Span<byte> header = stackalloc byte[QoiConstants.HeaderSize];
        ReadExact(header);

        // Validate magic
        if (header[0] != 'q' || header[1] != 'o' || header[2] != 'i' || header[3] != 'f')
        {
            throw new QoiDecodingException("Invalid QOI file magic. Expected 'qoif'.");
        }

        // Read dimensions (big-endian)
        _width = BinaryPrimitives.ReadInt32BigEndian(header.Slice(4, 4));
        _height = BinaryPrimitives.ReadInt32BigEndian(header.Slice(8, 4));
        _channels = header[12];
        _colorSpace = (QoiColorSpace)header[13];

        // Validate dimensions
        if (_width <= 0)
        {
            throw new QoiDecodingException($"Invalid width: {_width}");
        }

        if (_height <= 0 || _height >= QoiConstants.MaxPixels / _width)
        {
            throw new QoiDecodingException($"Invalid height: {_height}. Maximum for this width is {QoiConstants.MaxPixels / _width - 1}");
        }

        // Validate channels
        if (_channels != 3 && _channels != 4)
        {
            throw new QoiDecodingException($"Invalid number of channels: {_channels}. Must be 3 (RGB) or 4 (RGBA).");
        }

        // Validate color space
        if (_colorSpace != QoiColorSpace.SRgb && _colorSpace != QoiColorSpace.Linear)
        {
            throw new QoiDecodingException($"Invalid color space: {(byte)_colorSpace}. Must be 0 (sRGB) or 1 (linear).");
        }
    }

    private void DecodePixels(byte[] pixels)
    {
        // Hash table for previously seen pixels (packed as RGBA int)
        int[] hashTable = new int[QoiConstants.HashTableSize];

        // Initialize hash table with alpha = 255 for 3-channel images
        if (_channels == 3)
        {
            for (int i = 0; i < hashTable.Length; i++)
            {
                hashTable[i] = 255; // Alpha byte in lowest position
            }
        }

        // Current pixel state (start with r=0, g=0, b=0, a=255)
        byte r = 0, g = 0, b = 0, a = 255;
        int currentPixel = PackPixel(0, 0, 0, 255);

        int pixelCount = _width * _height;
        int pxPos = 0;

        while (pxPos < pixelCount)
        {
            byte b1 = ReadByte();

            // Check 8-bit tags first (they have priority)
            if (b1 == QoiConstants.Rgb)
            {
                // QOI_OP_RGB: full RGB values, alpha unchanged
                r = ReadByte();
                g = ReadByte();
                b = ReadByte();
                currentPixel = PackPixel(r, g, b, a);
            }
            else if (b1 == QoiConstants.Rgba)
            {
                // QOI_OP_RGBA: full RGBA values
                r = ReadByte();
                g = ReadByte();
                b = ReadByte();
                a = ReadByte();
                currentPixel = PackPixel(r, g, b, a);
            }
            else
            {
                // Check 2-bit tags
                byte tag = (byte)(b1 & QoiConstants.Mask2);

                if (tag == QoiConstants.Index)
                {
                    // QOI_OP_INDEX: use pixel from hash table
                    int index = b1 & 0x3F;
                    currentPixel = hashTable[index];
                    UnpackPixel(currentPixel, out r, out g, out b, out a);

                    // Write pixel and continue (don't update hash table)
                    WritePixel(pixels, pxPos++, r, g, b, a);
                    continue;
                }
                else if (tag == QoiConstants.Diff)
                {
                    // QOI_OP_DIFF: small difference (-2..1 per channel)
                    int dr = ((b1 >> 4) & 0x03) - 2;
                    int dg = ((b1 >> 2) & 0x03) - 2;
                    int db = (b1 & 0x03) - 2;
                    r = (byte)(r + dr);
                    g = (byte)(g + dg);
                    b = (byte)(b + db);
                    currentPixel = PackPixel(r, g, b, a);
                }
                else if (tag == QoiConstants.Luma)
                {
                    // QOI_OP_LUMA: luma-based difference
                    byte b2 = ReadByte();
                    int vg = (b1 & 0x3F) - 32;
                    int vr = vg + ((b2 >> 4) & 0x0F) - 8;
                    int vb = vg + (b2 & 0x0F) - 8;
                    r = (byte)(r + vr);
                    g = (byte)(g + vg);
                    b = (byte)(b + vb);
                    currentPixel = PackPixel(r, g, b, a);
                }
                else // tag == QoiConstants.Run
                {
                    // QOI_OP_RUN: repeat previous pixel
                    int runLength = (b1 & 0x3F) + 1;
                    for (int i = 0; i < runLength && pxPos < pixelCount; i++)
                    {
                        WritePixel(pixels, pxPos++, r, g, b, a);
                    }
                    continue;
                }
            }

            // Update hash table and write pixel
            int hashIndex = QoiConstants.CalculateHashIndex(r, g, b, a);
            hashTable[hashIndex] = currentPixel;
            WritePixel(pixels, pxPos++, r, g, b, a);
        }

        // Verify end marker (optional - some files may be truncated)
        VerifyEndMarker();
    }

    private void VerifyEndMarker()
    {
        try
        {
            Span<byte> padding = stackalloc byte[QoiConstants.Padding.Length];
            int bytesRead = 0;

            // Try to read the end marker
            while (bytesRead < padding.Length)
            {
                if (_bufferPos >= _bufferLength)
                {
                    _bufferLength = _stream.Read(_buffer, 0, _buffer.Length);
                    _bufferPos = 0;
                    if (_bufferLength == 0)
                    {
                        // End of stream before reading full padding - acceptable
                        return;
                    }
                }

                int available = _bufferLength - _bufferPos;
                int needed = padding.Length - bytesRead;
                int toCopy = Math.Min(available, needed);

                _buffer.AsSpan(_bufferPos, toCopy).CopyTo(padding.Slice(bytesRead));
                _bufferPos += toCopy;
                bytesRead += toCopy;
            }

            // Validate end marker
            for (int i = 0; i < QoiConstants.Padding.Length; i++)
            {
                if (padding[i] != QoiConstants.Padding[i])
                {
                    throw new QoiDecodingException("Invalid QOI end marker.");
                }
            }
        }
        catch (EndOfStreamException)
        {
            // End marker verification is optional
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int PackPixel(byte r, byte g, byte b, byte a)
    {
        return (r << 24) | (g << 16) | (b << 8) | a;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void UnpackPixel(int packed, out byte r, out byte g, out byte b, out byte a)
    {
        r = (byte)(packed >> 24);
        g = (byte)(packed >> 16);
        b = (byte)(packed >> 8);
        a = (byte)packed;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WritePixel(byte[] pixels, int index, byte r, byte g, byte b, byte a)
    {
        int offset = index * 4;
        pixels[offset] = r;
        pixels[offset + 1] = g;
        pixels[offset + 2] = b;
        pixels[offset + 3] = a;
    }

    private static bool HasMeaningfulAlpha(byte[] pixels)
    {
        // Check if any pixel has alpha != 255
        for (int i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 255)
            {
                return true;
            }
        }
        return false;
    }

    private void ReadExact(Span<byte> buffer)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            if (_bufferPos >= _bufferLength)
            {
                _bufferLength = _stream.Read(_buffer, 0, _buffer.Length);
                _bufferPos = 0;
                if (_bufferLength == 0)
                {
                    throw new QoiDecodingException("Unexpected end of QOI data.");
                }
            }

            int available = _bufferLength - _bufferPos;
            int needed = buffer.Length - totalRead;
            int toCopy = Math.Min(available, needed);

            _buffer.AsSpan(_bufferPos, toCopy).CopyTo(buffer.Slice(totalRead));
            _bufferPos += toCopy;
            totalRead += toCopy;
        }
    }

    private byte ReadByte()
    {
        if (_bufferPos >= _bufferLength)
        {
            _bufferLength = _stream.Read(_buffer, 0, _buffer.Length);
            _bufferPos = 0;
            if (_bufferLength == 0)
            {
                throw new QoiDecodingException("Unexpected end of QOI data.");
            }
        }
        return _buffer[_bufferPos++];
    }
}
