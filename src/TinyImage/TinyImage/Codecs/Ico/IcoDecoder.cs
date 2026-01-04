using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace TinyImage.Codecs.Ico;

/// <summary>
/// Decodes ICO and CUR image files.
/// </summary>
internal sealed class IcoDecoder
{
    // Size of the BITMAPINFOHEADER struct.
    private const int BmpHeaderLen = 40;

    // Size limits for images in an ICO file.
    private const uint MinWidth = 1;
    private const uint MinHeight = 1;
    private const ulong MaxPixels = 8192 * 8192;

    private readonly Stream _stream;
    private readonly byte[] _buffer = new byte[16];

    public IcoDecoder(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    /// <summary>
    /// Decodes the ICO/CUR file and returns all entries with their decoded images.
    /// </summary>
    public (IcoResourceType resourceType, List<IcoDirectoryEntry> entries, List<(uint width, uint height, byte[] rgba)> images) Decode()
    {
        // Get total file length for validation
        long fileLen = _stream.Length;
        _stream.Position = 0;

        // Read ICONDIR header (6 bytes)
        ReadExact(_buffer, 0, 6);

        ushort reserved = BinaryPrimitives.ReadUInt16LittleEndian(_buffer.AsSpan(0, 2));
        if (reserved != 0)
            throw new InvalidOperationException($"Invalid reserved field value in ICONDIR (was {reserved}, but must be 0)");

        ushort resType = BinaryPrimitives.ReadUInt16LittleEndian(_buffer.AsSpan(2, 2));
        IcoResourceType resourceType;
        if (resType == 1)
            resourceType = IcoResourceType.Icon;
        else if (resType == 2)
            resourceType = IcoResourceType.Cursor;
        else
            throw new InvalidOperationException($"Invalid resource type ({resType})");

        ushort numEntries = BinaryPrimitives.ReadUInt16LittleEndian(_buffer.AsSpan(4, 2));

        var entries = new List<IcoDirectoryEntry>(numEntries);
        var spans = new List<(uint offset, uint size)>(numEntries);

        // Read ICONDIRENTRY structs (16 bytes each)
        for (int i = 0; i < numEntries; i++)
        {
            ReadExact(_buffer, 0, 16);

            byte widthByte = _buffer[0];
            byte heightByte = _buffer[1];
            byte numColors = _buffer[2];
            byte entryReserved = _buffer[3];

            if (entryReserved != 0)
                throw new InvalidOperationException($"Invalid reserved field value in ICONDIRENTRY (was {entryReserved}, but must be 0)");

            ushort colorPlanes = BinaryPrimitives.ReadUInt16LittleEndian(_buffer.AsSpan(4, 2));
            ushort bitsPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(_buffer.AsSpan(6, 2));
            uint dataSize = BinaryPrimitives.ReadUInt32LittleEndian(_buffer.AsSpan(8, 4));
            uint dataOffset = BinaryPrimitives.ReadUInt32LittleEndian(_buffer.AsSpan(12, 4));

            // Validate data span
            if ((ulong)dataOffset + dataSize > (ulong)fileLen)
                throw new InvalidOperationException($"Image data span (offset={dataOffset}, size={dataSize}) exceeds file length ({fileLen})");

            // Width/height of 0 means 256 (or more for newer formats)
            uint width = widthByte == 0 ? 256u : widthByte;
            uint height = heightByte == 0 ? 256u : heightByte;

            spans.Add((dataOffset, dataSize));

            var entry = new IcoDirectoryEntry
            {
                ResourceType = resourceType,
                Width = width,
                Height = height,
                NumColors = numColors,
                ColorPlanesOrHotspotX = colorPlanes,
                BitsPerPixelOrHotspotY = bitsPerPixel
            };
            entries.Add(entry);
        }

        // Read image data for each entry
        for (int i = 0; i < spans.Count; i++)
        {
            var (dataOffset, dataSize) = spans[i];
            _stream.Position = dataOffset;
            var data = new byte[dataSize];
            ReadExact(data, 0, (int)dataSize);
            entries[i].Data = data;
        }

        // Update width/height from actual image data and decode images
        var images = new List<(uint width, uint height, byte[] rgba)>(entries.Count);

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];

            // Try to get actual dimensions from image data
            if (TryDecodeSize(entry, out uint actualWidth, out uint actualHeight))
            {
                entry.Width = actualWidth;
                entry.Height = actualHeight;
            }

            // Decode the image
            var (width, height, rgba) = DecodeEntry(entry);
            images.Add((width, height, rgba));
        }

        return (resourceType, entries, images);
    }

    /// <summary>
    /// Tries to decode just the size from the image data.
    /// </summary>
    private bool TryDecodeSize(IcoDirectoryEntry entry, out uint width, out uint height)
    {
        width = 0;
        height = 0;

        try
        {
            if (entry.IsPng)
            {
                return TryDecodePngSize(entry.Data, out width, out height);
            }
            else
            {
                return TryDecodeBmpSize(entry.Data, out width, out height);
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Tries to decode PNG dimensions from the IHDR chunk.
    /// </summary>
    private bool TryDecodePngSize(byte[] data, out uint width, out uint height)
    {
        width = 0;
        height = 0;

        // PNG: signature (8) + IHDR length (4) + "IHDR" (4) + width (4) + height (4)
        if (data.Length < 24)
            return false;

        // Skip signature and check IHDR chunk
        if (data[12] != 'I' || data[13] != 'H' || data[14] != 'D' || data[15] != 'R')
            return false;

        // PNG uses big-endian
        width = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(16, 4));
        height = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(20, 4));
        return true;
    }

    /// <summary>
    /// Tries to decode BMP dimensions from the header.
    /// </summary>
    private bool TryDecodeBmpSize(byte[] data, out uint width, out uint height)
    {
        width = 0;
        height = 0;

        if (data.Length < 12)
            return false;

        uint headerSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0, 4));
        if (headerSize != BmpHeaderLen)
            return false;

        int signedWidth = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4, 4));
        int signedHeight = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(8, 4));

        if (signedWidth < (int)MinWidth)
            return false;

        // Height is doubled in ICO format (includes AND mask)
        if (signedHeight % 2 != 0)
            return false;

        signedHeight /= 2;
        if (signedHeight < (int)MinHeight)
            return false;

        width = (uint)signedWidth;
        height = (uint)signedHeight;
        return true;
    }

    /// <summary>
    /// Decodes an entry's image data to RGBA.
    /// </summary>
    private (uint width, uint height, byte[] rgba) DecodeEntry(IcoDirectoryEntry entry)
    {
        if (entry.IsPng)
        {
            return DecodePng(entry.Data);
        }
        else
        {
            return DecodeBmp(entry.Data);
        }
    }

    /// <summary>
    /// Decodes PNG data to RGBA.
    /// </summary>
    private (uint width, uint height, byte[] rgba) DecodePng(byte[] data)
    {
        using var ms = new MemoryStream(data);
        var image = Png.PngCodec.Decode(ms);
        var buffer = image.GetBuffer();
        return ((uint)buffer.Width, (uint)buffer.Height, buffer.GetRawData());
    }

    /// <summary>
    /// Decodes ICO BMP data to RGBA.
    /// ICO BMP format differs from regular BMP: no file header, double height for AND mask.
    /// </summary>
    private (uint width, uint height, byte[] rgba) DecodeBmp(byte[] data)
    {
        int pos = 0;

        // Read BITMAPINFOHEADER
        uint headerSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos, 4));
        if (headerSize != BmpHeaderLen)
            throw new InvalidOperationException($"Invalid BMP header size (was {headerSize}, must be {BmpHeaderLen})");

        pos += 4;
        int signedWidth = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos, 4));
        pos += 4;
        int signedHeight = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos, 4));
        pos += 4;

        if (signedWidth < (int)MinWidth)
            throw new InvalidOperationException($"Invalid BMP width (was {signedWidth}, but must be at least {MinWidth})");

        uint width = (uint)signedWidth;

        if (signedHeight % 2 != 0)
            throw new InvalidOperationException($"Invalid height field in BMP header (was {signedHeight}, but must be divisible by 2)");

        signedHeight /= 2;
        if (signedHeight < (int)MinHeight)
            throw new InvalidOperationException($"Invalid BMP height (was {signedHeight}, but must be at least {MinHeight})");

        uint height = (uint)signedHeight;

        // Skip planes
        pos += 2;
        ushort bitsPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(pos, 2));
        pos += 2;

        // Skip compression, image size, ppm, colors
        pos += 20;

        // Determine color depth
        IcoBmpDepth? depthOpt = IcoBmpDepthExtensions.FromBitsPerPixel(bitsPerPixel);
        if (!depthOpt.HasValue)
            throw new InvalidOperationException($"Unsupported BMP bits-per-pixel ({bitsPerPixel})");

        IcoBmpDepth depth = depthOpt.Value;
        int numColors = depth.GetNumColors();

        // Read color table
        var colorTable = new (byte r, byte g, byte b)[numColors];
        for (int i = 0; i < numColors; i++)
        {
            byte blue = data[pos++];
            byte green = data[pos++];
            byte red = data[pos++];
            pos++; // reserved
            colorTable[i] = (red, green, blue);
        }

        // Validate dimensions
        ulong numPixels = (ulong)width * height;
        if (numPixels > MaxPixels)
            throw new InvalidOperationException($"Image dimensions too large ({width}x{height} = {numPixels} pixels, max is {MaxPixels})");

        // Allocate RGBA buffer (initialized to 255 alpha)
        var rgba = new byte[numPixels * 4];
        for (int i = 0; i < rgba.Length; i += 4)
        {
            rgba[i + 3] = 255;
        }

        // Calculate row sizes
        uint rowDataSize = ((width * bitsPerPixel) + 7) / 8;
        uint rowPaddingSize = ((rowDataSize + 3) / 4) * 4 - rowDataSize;

        // Read color data (bottom-up)
        for (uint row = 0; row < height; row++)
        {
            int startOffset = (int)((height - row - 1) * width * 4);

            switch (depth)
            {
                case IcoBmpDepth.One:
                    ReadRow1Bpp(data, ref pos, width, rowDataSize, startOffset, rgba, colorTable);
                    break;
                case IcoBmpDepth.Four:
                    ReadRow4Bpp(data, ref pos, width, rowDataSize, startOffset, rgba, colorTable);
                    break;
                case IcoBmpDepth.Eight:
                    ReadRow8Bpp(data, ref pos, width, startOffset, rgba, colorTable);
                    break;
                case IcoBmpDepth.Sixteen:
                    ReadRow16Bpp(data, ref pos, width, startOffset, rgba);
                    break;
                case IcoBmpDepth.TwentyFour:
                    ReadRow24Bpp(data, ref pos, width, startOffset, rgba);
                    break;
                case IcoBmpDepth.ThirtyTwo:
                    ReadRow32Bpp(data, ref pos, width, startOffset, rgba);
                    break;
            }

            pos += (int)rowPaddingSize;
        }

        // Read alpha mask (1 bit per pixel) for non-32bpp images
        if (depth != IcoBmpDepth.ThirtyTwo)
        {
            uint maskRowDataSize = (width + 7) / 8;
            uint maskRowPaddingSize = ((maskRowDataSize + 3) / 4) * 4 - maskRowDataSize;

            for (uint row = 0; row < height; row++)
            {
                int startOffset = (int)((height - row - 1) * width * 4);
                uint col = 0;

                for (uint i = 0; i < maskRowDataSize && pos < data.Length; i++)
                {
                    byte b = data[pos++];
                    for (int bit = 0; bit < 8 && col < width; bit++)
                    {
                        // AND mask: 1 = transparent, 0 = opaque
                        if (((b >> (7 - bit)) & 0x1) == 1)
                        {
                            rgba[startOffset + 3] = 0;
                        }
                        col++;
                        startOffset += 4;
                    }
                }

                pos += (int)maskRowPaddingSize;
            }
        }

        return (width, height, rgba);
    }

    private void ReadRow1Bpp(byte[] data, ref int pos, uint width, uint rowDataSize, int startOffset, byte[] rgba, (byte r, byte g, byte b)[] colorTable)
    {
        uint col = 0;
        for (uint i = 0; i < rowDataSize; i++)
        {
            byte b = data[pos++];
            for (int bit = 0; bit < 8 && col < width; bit++)
            {
                int index = (b >> (7 - bit)) & 0x1;
                var (r, g, blue) = colorTable[index];
                rgba[startOffset] = r;
                rgba[startOffset + 1] = g;
                rgba[startOffset + 2] = blue;
                col++;
                startOffset += 4;
            }
        }
    }

    private void ReadRow4Bpp(byte[] data, ref int pos, uint width, uint rowDataSize, int startOffset, byte[] rgba, (byte r, byte g, byte b)[] colorTable)
    {
        uint col = 0;
        for (uint i = 0; i < rowDataSize; i++)
        {
            byte b = data[pos++];
            for (int nibble = 0; nibble < 2 && col < width; nibble++)
            {
                int index = (b >> (4 * (1 - nibble))) & 0xF;
                var (r, g, blue) = colorTable[index];
                rgba[startOffset] = r;
                rgba[startOffset + 1] = g;
                rgba[startOffset + 2] = blue;
                col++;
                startOffset += 4;
            }
        }
    }

    private void ReadRow8Bpp(byte[] data, ref int pos, uint width, int startOffset, byte[] rgba, (byte r, byte g, byte b)[] colorTable)
    {
        for (uint i = 0; i < width; i++)
        {
            int index = data[pos++];
            var (r, g, b) = colorTable[index];
            rgba[startOffset] = r;
            rgba[startOffset + 1] = g;
            rgba[startOffset + 2] = b;
            startOffset += 4;
        }
    }

    private void ReadRow16Bpp(byte[] data, ref int pos, uint width, int startOffset, byte[] rgba)
    {
        for (uint i = 0; i < width; i++)
        {
            ushort color = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(pos, 2));
            pos += 2;

            int red = (color >> 10) & 0x1F;
            int green = (color >> 5) & 0x1F;
            int blue = color & 0x1F;

            // Scale from 5-bit to 8-bit
            rgba[startOffset] = (byte)((red * 255 + 15) / 31);
            rgba[startOffset + 1] = (byte)((green * 255 + 15) / 31);
            rgba[startOffset + 2] = (byte)((blue * 255 + 15) / 31);
            startOffset += 4;
        }
    }

    private void ReadRow24Bpp(byte[] data, ref int pos, uint width, int startOffset, byte[] rgba)
    {
        for (uint i = 0; i < width; i++)
        {
            byte blue = data[pos++];
            byte green = data[pos++];
            byte red = data[pos++];
            rgba[startOffset] = red;
            rgba[startOffset + 1] = green;
            rgba[startOffset + 2] = blue;
            startOffset += 4;
        }
    }

    private void ReadRow32Bpp(byte[] data, ref int pos, uint width, int startOffset, byte[] rgba)
    {
        for (uint i = 0; i < width; i++)
        {
            byte blue = data[pos++];
            byte green = data[pos++];
            byte red = data[pos++];
            byte alpha = data[pos++];
            rgba[startOffset] = red;
            rgba[startOffset + 1] = green;
            rgba[startOffset + 2] = blue;
            rgba[startOffset + 3] = alpha;
            startOffset += 4;
        }
    }

    private void ReadExact(byte[] buffer, int offset, int count)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = _stream.Read(buffer, offset + totalRead, count - totalRead);
            if (read == 0)
                throw new EndOfStreamException("Unexpected end of stream while reading ICO file.");
            totalRead += read;
        }
    }
}
