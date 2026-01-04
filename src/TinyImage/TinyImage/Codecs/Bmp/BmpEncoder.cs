using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace TinyImage.Codecs.Bmp;

/// <summary>
/// Encodes image data to BMP format.
/// Supports 1, 4, 8, 16, 24, 32, and 64 bits per pixel output.
/// Uses BITMAPINFOHEADER (V3) format for maximum compatibility,
/// or V5 when ICC profile is specified.
/// </summary>
internal sealed class BmpEncoder
{
    private readonly Stream _stream;
    private readonly int _width;
    private readonly int _height;
    private readonly byte[] _pixels;
    private readonly BmpBitsPerPixel _bitsPerPixel;
    private readonly bool _hasAlpha;

    private Rgba32[]? _palette;
    private Dictionary<uint, int>? _colorLookup;
    
    /// <summary>
    /// Gets or sets the ICC profile to embed (requires V5 header).
    /// </summary>
    public byte[]? IccProfile { get; set; }
    
    /// <summary>
    /// Gets or sets the rendering intent for V5 headers.
    /// </summary>
    public BmpRenderingIntent RenderingIntent { get; set; } = BmpRenderingIntent.None;
    
    /// <summary>
    /// Gets or sets the 64-bit conversion mode for encoding.
    /// </summary>
    public Bmp64BitConverter.ConversionMode Conversion64Mode { get; set; } = Bmp64BitConverter.ConversionMode.ToSrgb;

    public BmpEncoder(Stream stream, int width, int height, byte[] pixels, BmpBitsPerPixel bitsPerPixel, bool hasAlpha)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _width = width;
        _height = height;
        _pixels = pixels ?? throw new ArgumentNullException(nameof(pixels));
        _bitsPerPixel = bitsPerPixel;
        _hasAlpha = hasAlpha;
    }

    /// <summary>
    /// Encodes the image to the output stream.
    /// </summary>
    public void Encode()
    {
        // Build palette if needed
        if (_bitsPerPixel <= BmpBitsPerPixel.Bit8)
        {
            BuildPalette();
        }

        int bytesPerRow = CalculateBytesPerRow();
        int padding = (4 - (bytesPerRow % 4)) % 4;
        int rowStride = bytesPerRow + padding;
        int rawDataSize = rowStride * _height;
        int paletteSize = GetPaletteSize();
        int iccProfileSize = IccProfile?.Length ?? 0;

        // Calculate header size based on format
        int infoHeaderSize;
        if (IccProfile != null && IccProfile.Length > 0)
        {
            // V5 header required for ICC profile
            infoHeaderSize = BmpConstants.HeaderSizes.InfoHeaderV5;
        }
        else if (_bitsPerPixel == BmpBitsPerPixel.Bit32 && _hasAlpha)
        {
            // V4 for 32-bit with alpha mask
            infoHeaderSize = BmpConstants.HeaderSizes.InfoHeaderV4;
        }
        else if (_bitsPerPixel == BmpBitsPerPixel.Bit64)
        {
            // V4 minimum for 64-bit (needs color space info)
            infoHeaderSize = BmpConstants.HeaderSizes.InfoHeaderV4;
        }
        else
        {
            infoHeaderSize = BmpConstants.HeaderSizes.InfoHeaderV3;
        }

        int pixelDataOffset = BmpFileHeader.Size + infoHeaderSize + paletteSize;
        int fileSize = pixelDataOffset + rawDataSize + iccProfileSize;

        // Write file header
        WriteFileHeader(fileSize, pixelDataOffset);

        // Write info header
        WriteInfoHeader(infoHeaderSize, rawDataSize, iccProfileSize);

        // Write palette if needed
        WritePalette();

        // Write pixel data
        WritePixelData(rowStride, padding);
        
        // Write ICC profile at end if present
        if (IccProfile != null && IccProfile.Length > 0)
        {
            _stream.Write(IccProfile, 0, IccProfile.Length);
        }
    }

    private void BuildPalette()
    {
        int maxColors = 1 << (int)_bitsPerPixel;
        var colorSet = new Dictionary<uint, int>();
        var paletteList = new List<Rgba32>();

        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                int offset = (y * _width + x) * 4;
                byte r = _pixels[offset];
                byte g = _pixels[offset + 1];
                byte b = _pixels[offset + 2];

                // Quantize to reduce colors if needed
                uint colorKey = ((uint)r << 16) | ((uint)g << 8) | b;

                if (!colorSet.ContainsKey(colorKey))
                {
                    if (paletteList.Count >= maxColors)
                    {
                        // Find closest color (simple nearest neighbor)
                        continue;
                    }
                    colorSet[colorKey] = paletteList.Count;
                    paletteList.Add(new Rgba32(r, g, b, 255));
                }
            }
        }

        // If we need exactly 2, 4, 16, or 256 colors but have fewer, pad the palette
        while (paletteList.Count < maxColors)
        {
            paletteList.Add(new Rgba32(0, 0, 0, 255));
        }

        _palette = paletteList.ToArray();
        _colorLookup = colorSet;
    }

    private int CalculateBytesPerRow()
    {
        return _bitsPerPixel switch
        {
            BmpBitsPerPixel.Bit1 => (_width + 7) / 8,
            BmpBitsPerPixel.Bit2 => (_width + 3) / 4,
            BmpBitsPerPixel.Bit4 => (_width + 1) / 2,
            BmpBitsPerPixel.Bit8 => _width,
            BmpBitsPerPixel.Bit16 => _width * 2,
            BmpBitsPerPixel.Bit24 => _width * 3,
            BmpBitsPerPixel.Bit32 => _width * 4,
            BmpBitsPerPixel.Bit64 => _width * 8,
            _ => _width * 3
        };
    }

    private int GetPaletteSize()
    {
        if (_bitsPerPixel > BmpBitsPerPixel.Bit8)
            return 0;

        int colorCount = 1 << (int)_bitsPerPixel;
        return colorCount * 4; // 4 bytes per palette entry
    }

    private void WriteFileHeader(int fileSize, int pixelDataOffset)
    {
        byte[] header = new byte[BmpFileHeader.Size];

        var fileHeader = new BmpFileHeader(
            type: BmpConstants.TypeMarkers.Bitmap,
            fileSize: fileSize,
            reserved1: 0,
            reserved2: 0,
            pixelDataOffset: pixelDataOffset
        );

        fileHeader.WriteTo(header);
        _stream.Write(header, 0, header.Length);
    }

    private void WriteInfoHeader(int headerSize, int rawDataSize, int iccProfileSize = 0)
    {
        int colorCount = _bitsPerPixel <= BmpBitsPerPixel.Bit8 ? 1 << (int)_bitsPerPixel : 0;

        var infoHeader = new BmpInfoHeader
        {
            HeaderSize = headerSize,
            Width = _width,
            Height = _height, // Positive = bottom-up
            Planes = 1,
            BitsPerPixel = (ushort)_bitsPerPixel,
            Compression = BmpCompression.RGB,
            ImageSize = rawDataSize,
            XPelsPerMeter = 2835, // ~72 DPI
            YPelsPerMeter = 2835,
            ColorsUsed = colorCount,
            ColorsImportant = 0,
            ColorSpaceType = BmpColorSpace.LCS_sRGB
        };

        // For 32-bit with alpha, use BitFields compression
        if (_bitsPerPixel == BmpBitsPerPixel.Bit32 && _hasAlpha)
        {
            infoHeader.Compression = BmpCompression.BitFields;
            infoHeader.RedMask = 0x00FF0000;
            infoHeader.GreenMask = 0x0000FF00;
            infoHeader.BlueMask = 0x000000FF;
            infoHeader.AlphaMask = 0xFF000000;
        }
        
        // For 64-bit, no special compression but need color space info
        if (_bitsPerPixel == BmpBitsPerPixel.Bit64)
        {
            // 64-bit uses BI_RGB with implicit s2.13 BGRA format
            infoHeader.Compression = BmpCompression.RGB;
        }
        
        // ICC profile handling for V5 headers
        if (iccProfileSize > 0 && headerSize == BmpConstants.HeaderSizes.InfoHeaderV5)
        {
            infoHeader.ColorSpaceType = BmpColorSpace.PROFILE_EMBEDDED;
            // Profile offset is relative to start of header (V5 header ends at 124 bytes)
            // Profile is written after pixel data, so offset = header size - 14 (file header is separate)
            // Actually, offset is from start of BITMAPV5HEADER, profile is after pixel data
            int paletteSize = GetPaletteSize();
            int bytesPerRow = CalculateBytesPerRow();
            int padding = (4 - (bytesPerRow % 4)) % 4;
            int rowStride = bytesPerRow + padding;
            infoHeader.ProfileDataOffset = headerSize + paletteSize + (rowStride * _height);
            infoHeader.ProfileSize = iccProfileSize;
            infoHeader.RenderingIntent = RenderingIntent;
        }

        if (headerSize == BmpConstants.HeaderSizes.InfoHeaderV5)
        {
            byte[] header = new byte[BmpConstants.HeaderSizes.InfoHeaderV5];
            infoHeader.WriteV5Header(header);
            _stream.Write(header, 0, header.Length);
        }
        else if (headerSize == BmpConstants.HeaderSizes.InfoHeaderV4)
        {
            byte[] header = new byte[BmpConstants.HeaderSizes.InfoHeaderV4];
            infoHeader.WriteV4Header(header);
            _stream.Write(header, 0, header.Length);
        }
        else
        {
            byte[] header = new byte[BmpConstants.HeaderSizes.InfoHeaderV3];
            infoHeader.WriteV3Header(header);
            _stream.Write(header, 0, header.Length);
        }
    }

    private void WritePalette()
    {
        if (_palette == null)
            return;

        byte[] entry = new byte[4];
        foreach (var color in _palette)
        {
            entry[0] = color.B;
            entry[1] = color.G;
            entry[2] = color.R;
            entry[3] = 0; // Reserved
            _stream.Write(entry, 0, 4);
        }
    }

    private void WritePixelData(int rowStride, int padding)
    {
        byte[] rowBuffer = new byte[rowStride];
        byte[] paddingBytes = new byte[4];

        // BMP stores rows bottom-up
        for (int y = _height - 1; y >= 0; y--)
        {
            switch (_bitsPerPixel)
            {
                case BmpBitsPerPixel.Bit1:
                    EncodeRow1Bit(y, rowBuffer);
                    break;
                case BmpBitsPerPixel.Bit2:
                    EncodeRow2Bit(y, rowBuffer);
                    break;
                case BmpBitsPerPixel.Bit4:
                    EncodeRow4Bit(y, rowBuffer);
                    break;
                case BmpBitsPerPixel.Bit8:
                    EncodeRow8Bit(y, rowBuffer);
                    break;
                case BmpBitsPerPixel.Bit16:
                    EncodeRow16Bit(y, rowBuffer);
                    break;
                case BmpBitsPerPixel.Bit24:
                    EncodeRow24Bit(y, rowBuffer);
                    break;
                case BmpBitsPerPixel.Bit32:
                    EncodeRow32Bit(y, rowBuffer);
                    break;
                case BmpBitsPerPixel.Bit64:
                    EncodeRow64Bit(y, rowBuffer);
                    break;
            }

            _stream.Write(rowBuffer, 0, rowStride - padding);
            if (padding > 0)
                _stream.Write(paddingBytes, 0, padding);
        }
    }

    private void EncodeRow1Bit(int y, byte[] rowBuffer)
    {
        Array.Clear(rowBuffer, 0, rowBuffer.Length);

        for (int x = 0; x < _width; x++)
        {
            int pixelOffset = (y * _width + x) * 4;
            byte r = _pixels[pixelOffset];
            byte g = _pixels[pixelOffset + 1];
            byte b = _pixels[pixelOffset + 2];

            // Convert to grayscale and threshold
            int brightness = (r * 299 + g * 587 + b * 114) / 1000;
            int colorIndex = brightness > 127 ? 0 : 1;

            int byteIndex = x / 8;
            int bitIndex = 7 - (x % 8);
            rowBuffer[byteIndex] |= (byte)(colorIndex << bitIndex);
        }
    }

    private void EncodeRow2Bit(int y, byte[] rowBuffer)
    {
        Array.Clear(rowBuffer, 0, rowBuffer.Length);

        for (int x = 0; x < _width; x++)
        {
            int pixelOffset = (y * _width + x) * 4;
            byte r = _pixels[pixelOffset];
            byte g = _pixels[pixelOffset + 1];
            byte b = _pixels[pixelOffset + 2];

            uint colorKey = ((uint)r << 16) | ((uint)g << 8) | b;
            int colorIndex = _colorLookup!.TryGetValue(colorKey, out int idx) ? idx : FindClosestColor(r, g, b);

            int byteIndex = x / 4;
            int shift = (3 - (x % 4)) * 2;
            rowBuffer[byteIndex] |= (byte)((colorIndex & 0x03) << shift);
        }
    }

    private void EncodeRow4Bit(int y, byte[] rowBuffer)
    {
        Array.Clear(rowBuffer, 0, rowBuffer.Length);

        for (int x = 0; x < _width; x++)
        {
            int pixelOffset = (y * _width + x) * 4;
            byte r = _pixels[pixelOffset];
            byte g = _pixels[pixelOffset + 1];
            byte b = _pixels[pixelOffset + 2];

            uint colorKey = ((uint)r << 16) | ((uint)g << 8) | b;
            int colorIndex = _colorLookup!.TryGetValue(colorKey, out int idx) ? idx : FindClosestColor(r, g, b);

            int byteIndex = x / 2;
            if ((x & 1) == 0)
                rowBuffer[byteIndex] = (byte)((colorIndex & 0x0F) << 4);
            else
                rowBuffer[byteIndex] |= (byte)(colorIndex & 0x0F);
        }
    }

    private void EncodeRow8Bit(int y, byte[] rowBuffer)
    {
        for (int x = 0; x < _width; x++)
        {
            int pixelOffset = (y * _width + x) * 4;
            byte r = _pixels[pixelOffset];
            byte g = _pixels[pixelOffset + 1];
            byte b = _pixels[pixelOffset + 2];

            uint colorKey = ((uint)r << 16) | ((uint)g << 8) | b;
            int colorIndex = _colorLookup!.TryGetValue(colorKey, out int idx) ? idx : FindClosestColor(r, g, b);
            rowBuffer[x] = (byte)colorIndex;
        }
    }

    private void EncodeRow16Bit(int y, byte[] rowBuffer)
    {
        for (int x = 0; x < _width; x++)
        {
            int pixelOffset = (y * _width + x) * 4;
            byte r = _pixels[pixelOffset];
            byte g = _pixels[pixelOffset + 1];
            byte b = _pixels[pixelOffset + 2];

            // Convert to 5-5-5 format
            ushort r5 = (ushort)((r >> 3) & 0x1F);
            ushort g5 = (ushort)((g >> 3) & 0x1F);
            ushort b5 = (ushort)((b >> 3) & 0x1F);
            ushort pixel = (ushort)((r5 << 10) | (g5 << 5) | b5);

            int offset = x * 2;
            BinaryPrimitives.WriteUInt16LittleEndian(rowBuffer.AsSpan(offset), pixel);
        }
    }

    private void EncodeRow24Bit(int y, byte[] rowBuffer)
    {
        for (int x = 0; x < _width; x++)
        {
            int pixelOffset = (y * _width + x) * 4;
            int rowOffset = x * 3;

            rowBuffer[rowOffset] = _pixels[pixelOffset + 2];     // B
            rowBuffer[rowOffset + 1] = _pixels[pixelOffset + 1]; // G
            rowBuffer[rowOffset + 2] = _pixels[pixelOffset];     // R
        }
    }

    private void EncodeRow32Bit(int y, byte[] rowBuffer)
    {
        for (int x = 0; x < _width; x++)
        {
            int pixelOffset = (y * _width + x) * 4;
            int rowOffset = x * 4;

            rowBuffer[rowOffset] = _pixels[pixelOffset + 2];     // B
            rowBuffer[rowOffset + 1] = _pixels[pixelOffset + 1]; // G
            rowBuffer[rowOffset + 2] = _pixels[pixelOffset];     // R
            rowBuffer[rowOffset + 3] = _pixels[pixelOffset + 3]; // A
        }
    }
    
    private void EncodeRow64Bit(int y, byte[] rowBuffer)
    {
        // 64-bit: 16-bit per channel in BGRA order, s2.13 fixed-point, linear light
        for (int x = 0; x < _width; x++)
        {
            int pixelOffset = (y * _width + x) * 4;
            int rowOffset = x * 8;

            var color = new Rgba32(
                _pixels[pixelOffset],
                _pixels[pixelOffset + 1],
                _pixels[pixelOffset + 2],
                _pixels[pixelOffset + 3]
            );

            // Convert from 8-bit sRGB to 16-bit s2.13 linear light
            Bmp64BitConverter.ConvertToS2_13(color, Conversion64Mode, 
                out ushort r, out ushort g, out ushort b, out ushort a);

            // Write in BGRA order
            BinaryPrimitives.WriteUInt16LittleEndian(rowBuffer.AsSpan(rowOffset), b);
            BinaryPrimitives.WriteUInt16LittleEndian(rowBuffer.AsSpan(rowOffset + 2), g);
            BinaryPrimitives.WriteUInt16LittleEndian(rowBuffer.AsSpan(rowOffset + 4), r);
            BinaryPrimitives.WriteUInt16LittleEndian(rowBuffer.AsSpan(rowOffset + 6), a);
        }
    }

    private int FindClosestColor(byte r, byte g, byte b)
    {
        if (_palette == null) return 0;

        int bestIndex = 0;
        int bestDistance = int.MaxValue;

        for (int i = 0; i < _palette.Length; i++)
        {
            var c = _palette[i];
            int dr = r - c.R;
            int dg = g - c.G;
            int db = b - c.B;
            int distance = dr * dr + dg * dg + db * db;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        return bestIndex;
    }
}
