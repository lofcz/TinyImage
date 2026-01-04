using System;
using System.Buffers.Binary;
using System.IO;

namespace TinyImage.Codecs.Tga;

/// <summary>
/// Decodes TGA image data from a stream.
/// Supports all standard image types (uncompressed and RLE), bit depths, and color maps.
/// </summary>
internal sealed class TgaDecoder
{
    private readonly Stream _stream;
    private readonly byte[] _buffer;
    private int _bufferPos;
    private int _bufferLength;

    private TgaHeader _header = null!;
    private byte[]? _imageId;
    private byte[]? _colorMap;

    // Output
    private int _width;
    private int _height;
    private byte[]? _pixels;
    private bool _hasAlpha;

    public TgaDecoder(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _buffer = new byte[8192];
        _bufferPos = 0;
        _bufferLength = 0;
    }

    /// <summary>
    /// Decodes the TGA image and returns the RGBA pixel data.
    /// </summary>
    public (int width, int height, byte[] pixels, bool hasAlpha) Decode()
    {
        ReadHeader();
        ReadImageId();
        ReadColorMap();
        ReadPixelData();
        ApplyOrientation();

        return (_width, _height, _pixels!, _hasAlpha);
    }

    private void ReadHeader()
    {
        Span<byte> headerBytes = stackalloc byte[TgaHeader.Size];
        ReadExact(headerBytes);
        _header = TgaHeader.Parse(headerBytes);

        _width = _header.Width;
        _height = _header.Height;

        // Validate dimensions
        if (_width <= 0 || _height <= 0)
            throw new InvalidOperationException("Invalid TGA image dimensions.");

        // Determine if image has alpha
        // 32-bit true-color typically has alpha
        // 16-bit can have 1-bit alpha (bit 15)
        // Check alpha bits in image descriptor
        _hasAlpha = _header.PixelDepth == 32 ||
                    (_header.PixelDepth == 16 && _header.AlphaBits > 0) ||
                    (_header.HasColorMap && _header.ColorMapDepth == 32);
    }

    private void ReadImageId()
    {
        if (_header.IdLength > 0)
        {
            _imageId = new byte[_header.IdLength];
            ReadExact(_imageId);
        }
    }

    private void ReadColorMap()
    {
        if (!_header.HasColorMap || _header.ColorMapLength == 0)
            return;

        int entryBytes = _header.ColorMapEntryBytes;
        int colorMapSize = _header.ColorMapLength * entryBytes;
        _colorMap = new byte[colorMapSize];

        // Skip to first entry if origin is specified
        if (_header.ColorMapOrigin > 0)
        {
            int skipBytes = _header.ColorMapOrigin * entryBytes;
            for (int i = 0; i < skipBytes; i++)
                ReadByte();
        }

        ReadExact(_colorMap.AsSpan(0, (_header.ColorMapLength - _header.ColorMapOrigin) * entryBytes));
    }

    private void ReadPixelData()
    {
        _pixels = new byte[_width * _height * 4];

        if (_header.ImageType == TgaImageType.NoData)
        {
            // No image data - leave pixels as transparent black
            return;
        }

        if (_header.IsCompressed)
        {
            DecodeRle();
        }
        else
        {
            DecodeUncompressed();
        }

        // Apply color map if needed
        if (_header.IsColorMapped && _colorMap != null)
        {
            ApplyColorMap();
        }
    }

    private void DecodeUncompressed()
    {
        int bytesPerPixel = _header.BytesPerPixel;
        int rowBytes = _width * bytesPerPixel;
        byte[] rowBuffer = new byte[rowBytes];

        for (int y = 0; y < _height; y++)
        {
            ReadExact(rowBuffer);
            DecodeRow(rowBuffer, y);
        }
    }

    private void DecodeRle()
    {
        int bytesPerPixel = _header.BytesPerPixel;
        byte[] pixelBuffer = new byte[bytesPerPixel];
        int x = 0;
        int y = 0;

        while (y < _height)
        {
            byte packetHeader = ReadByte();
            int count = (packetHeader & 0x7F) + 1;
            bool isRle = (packetHeader & 0x80) != 0;

            if (isRle)
            {
                // RLE packet: one pixel repeated 'count' times
                ReadExact(pixelBuffer);
                DecodePixel(pixelBuffer, 0, out byte r, out byte g, out byte b, out byte a);

                for (int i = 0; i < count; i++)
                {
                    SetPixel(x, y, r, g, b, a);
                    x++;
                    if (x >= _width)
                    {
                        x = 0;
                        y++;
                        if (y >= _height) break;
                    }
                }
            }
            else
            {
                // Raw packet: 'count' literal pixels
                for (int i = 0; i < count; i++)
                {
                    ReadExact(pixelBuffer);
                    DecodePixel(pixelBuffer, 0, out byte r, out byte g, out byte b, out byte a);
                    SetPixel(x, y, r, g, b, a);
                    x++;
                    if (x >= _width)
                    {
                        x = 0;
                        y++;
                        if (y >= _height) break;
                    }
                }
            }
        }
    }

    private void DecodeRow(byte[] rowBuffer, int y)
    {
        int bytesPerPixel = _header.BytesPerPixel;

        for (int x = 0; x < _width; x++)
        {
            int offset = x * bytesPerPixel;
            DecodePixel(rowBuffer, offset, out byte r, out byte g, out byte b, out byte a);
            SetPixel(x, y, r, g, b, a);
        }
    }

    private void DecodePixel(byte[] buffer, int offset, out byte r, out byte g, out byte b, out byte a)
    {
        // For color-mapped images, the pixel is just an index
        // We'll decode it later when applying the color map
        if (_header.IsColorMapped)
        {
            // Store the index in R, fill G,B,A with 0
            r = buffer[offset];
            g = 0;
            b = 0;
            a = 255;
            return;
        }

        if (_header.IsGrayscale)
        {
            DecodeGrayscalePixel(buffer, offset, out r, out g, out b, out a);
            return;
        }

        // True-color pixel
        switch (_header.PixelDepth)
        {
            case 16:
                Decode16BitPixel(buffer, offset, out r, out g, out b, out a);
                break;
            case 24:
                Decode24BitPixel(buffer, offset, out r, out g, out b, out a);
                break;
            case 32:
                Decode32BitPixel(buffer, offset, out r, out g, out b, out a);
                break;
            default:
                throw new NotSupportedException($"Unsupported TGA pixel depth: {_header.PixelDepth}");
        }
    }

    private void DecodeGrayscalePixel(byte[] buffer, int offset, out byte r, out byte g, out byte b, out byte a)
    {
        switch (_header.PixelDepth)
        {
            case 8:
                // 8-bit grayscale
                r = g = b = buffer[offset];
                a = 255;
                break;
            case 16:
                // 16-bit grayscale with alpha
                r = g = b = buffer[offset];
                a = buffer[offset + 1];
                break;
            default:
                throw new NotSupportedException($"Unsupported grayscale pixel depth: {_header.PixelDepth}");
        }
    }

    private void Decode16BitPixel(byte[] buffer, int offset, out byte r, out byte g, out byte b, out byte a)
    {
        // 16-bit: 5-5-5 RGB with optional 1-bit alpha
        // Format: ARRRRRGG GGGBBBBB (little-endian: low byte first)
        ushort pixel = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(offset));

        // Extract 5-bit values
        int b5 = pixel & 0x1F;
        int g5 = (pixel >> 5) & 0x1F;
        int r5 = (pixel >> 10) & 0x1F;

        // Scale from 5-bit to 8-bit: (v << 3) | (v >> 2)
        // This maps 0->0 and 31->255 correctly
        r = (byte)((r5 << 3) | (r5 >> 2));
        g = (byte)((g5 << 3) | (g5 >> 2));
        b = (byte)((b5 << 3) | (b5 >> 2));

        // Alpha bit (bit 15): 0 = transparent, 1 = opaque (or no alpha)
        if (_header.AlphaBits > 0)
        {
            a = (pixel & 0x8000) != 0 ? (byte)255 : (byte)0;
        }
        else
        {
            a = 255;
        }
    }

    private void Decode24BitPixel(byte[] buffer, int offset, out byte r, out byte g, out byte b, out byte a)
    {
        // 24-bit: BGR order
        b = buffer[offset];
        g = buffer[offset + 1];
        r = buffer[offset + 2];
        a = 255;
    }

    private void Decode32BitPixel(byte[] buffer, int offset, out byte r, out byte g, out byte b, out byte a)
    {
        // 32-bit: BGRA order
        b = buffer[offset];
        g = buffer[offset + 1];
        r = buffer[offset + 2];
        a = buffer[offset + 3];
    }

    private void ApplyColorMap()
    {
        if (_colorMap == null)
            return;

        int colorMapEntryBytes = _header.ColorMapEntryBytes;
        byte[] newPixels = new byte[_width * _height * 4];

        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                int pixelOffset = (y * _width + x) * 4;
                int colorIndex = _pixels![pixelOffset]; // Index stored in R channel

                // Bounds check
                if (colorIndex >= _header.ColorMapLength)
                {
                    // Invalid index - use black
                    newPixels[pixelOffset] = 0;
                    newPixels[pixelOffset + 1] = 0;
                    newPixels[pixelOffset + 2] = 0;
                    newPixels[pixelOffset + 3] = 255;
                    continue;
                }

                int mapOffset = colorIndex * colorMapEntryBytes;

                // Decode color from color map based on depth
                switch (_header.ColorMapDepth)
                {
                    case 15:
                    case 16:
                        DecodeColorMap16(mapOffset, out byte r16, out byte g16, out byte b16, out byte a16);
                        newPixels[pixelOffset] = r16;
                        newPixels[pixelOffset + 1] = g16;
                        newPixels[pixelOffset + 2] = b16;
                        newPixels[pixelOffset + 3] = a16;
                        break;
                    case 24:
                        newPixels[pixelOffset] = _colorMap[mapOffset + 2]; // R
                        newPixels[pixelOffset + 1] = _colorMap[mapOffset + 1]; // G
                        newPixels[pixelOffset + 2] = _colorMap[mapOffset]; // B
                        newPixels[pixelOffset + 3] = 255;
                        break;
                    case 32:
                        newPixels[pixelOffset] = _colorMap[mapOffset + 2]; // R
                        newPixels[pixelOffset + 1] = _colorMap[mapOffset + 1]; // G
                        newPixels[pixelOffset + 2] = _colorMap[mapOffset]; // B
                        newPixels[pixelOffset + 3] = _colorMap[mapOffset + 3]; // A
                        break;
                    default:
                        throw new NotSupportedException($"Unsupported color map depth: {_header.ColorMapDepth}");
                }
            }
        }

        _pixels = newPixels;
    }

    private void DecodeColorMap16(int mapOffset, out byte r, out byte g, out byte b, out byte a)
    {
        ushort pixel = BinaryPrimitives.ReadUInt16LittleEndian(_colorMap.AsSpan(mapOffset));

        int b5 = pixel & 0x1F;
        int g5 = (pixel >> 5) & 0x1F;
        int r5 = (pixel >> 10) & 0x1F;

        r = (byte)((r5 << 3) | (r5 >> 2));
        g = (byte)((g5 << 3) | (g5 >> 2));
        b = (byte)((b5 << 3) | (b5 >> 2));

        // For 16-bit color map with alpha bit
        if (_header.ColorMapDepth == 16 && _hasAlpha)
        {
            a = (pixel & 0x8000) != 0 ? (byte)255 : (byte)0;
        }
        else
        {
            a = 255;
        }
    }

    private void ApplyOrientation()
    {
        var orientation = _header.Orientation;

        // Apply horizontal flip if needed (right origins)
        if (orientation == TgaOrientation.BottomRight || orientation == TgaOrientation.TopRight)
        {
            FlipHorizontal();
        }

        // Apply vertical flip if needed (bottom origins are stored bottom-to-top)
        // TGA default is bottom-left, but we want top-left for our output
        if (orientation == TgaOrientation.BottomLeft || orientation == TgaOrientation.BottomRight)
        {
            FlipVertical();
        }
    }

    private void FlipHorizontal()
    {
        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width / 2; x++)
            {
                int leftOffset = (y * _width + x) * 4;
                int rightOffset = (y * _width + (_width - 1 - x)) * 4;

                // Swap pixels
                for (int c = 0; c < 4; c++)
                {
                    byte temp = _pixels![leftOffset + c];
                    _pixels[leftOffset + c] = _pixels[rightOffset + c];
                    _pixels[rightOffset + c] = temp;
                }
            }
        }
    }

    private void FlipVertical()
    {
        int rowBytes = _width * 4;
        byte[] tempRow = new byte[rowBytes];

        for (int y = 0; y < _height / 2; y++)
        {
            int topOffset = y * rowBytes;
            int bottomOffset = (_height - 1 - y) * rowBytes;

            // Copy top row to temp
            Array.Copy(_pixels!, topOffset, tempRow, 0, rowBytes);
            // Copy bottom row to top
            Array.Copy(_pixels!, bottomOffset, _pixels!, topOffset, rowBytes);
            // Copy temp to bottom
            Array.Copy(tempRow, 0, _pixels!, bottomOffset, rowBytes);
        }
    }

    private void SetPixel(int x, int y, byte r, byte g, byte b, byte a)
    {
        int offset = (y * _width + x) * 4;
        _pixels![offset] = r;
        _pixels[offset + 1] = g;
        _pixels[offset + 2] = b;
        _pixels[offset + 3] = a;
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
                    throw new EndOfStreamException("Unexpected end of TGA data.");
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
                throw new EndOfStreamException("Unexpected end of TGA data.");
        }
        return _buffer[_bufferPos++];
    }
}
