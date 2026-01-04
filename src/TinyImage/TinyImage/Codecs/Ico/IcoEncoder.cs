using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace TinyImage.Codecs.Ico;

/// <summary>
/// Encodes ICO and CUR image files.
/// </summary>
internal sealed class IcoEncoder
{
    // Size of the BITMAPINFOHEADER struct.
    private const int BmpHeaderLen = 40;

    private readonly Stream _stream;
    private readonly byte[] _writeBuffer = new byte[16];

    public IcoEncoder(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    /// <summary>
    /// Encodes multiple images into an ICO/CUR file.
    /// </summary>
    public void Encode(
        IcoResourceType resourceType,
        IReadOnlyList<(int width, int height, byte[] rgba, (ushort x, ushort y)? hotspot)> images)
    {
        if (images.Count == 0)
            throw new ArgumentException("At least one image is required.", nameof(images));
        if (images.Count > ushort.MaxValue)
            throw new ArgumentException($"Too many images (was {images.Count}, max is {ushort.MaxValue}).", nameof(images));

        // Encode all images first
        var entries = new List<IcoDirectoryEntry>(images.Count);
        foreach (var (width, height, rgba, hotspot) in images)
        {
            var entry = EncodeImage(resourceType, width, height, rgba, hotspot);
            entries.Add(entry);
        }

        // Write ICONDIR header
        BinaryPrimitives.WriteUInt16LittleEndian(_writeBuffer.AsSpan(0, 2), 0); // reserved
        BinaryPrimitives.WriteUInt16LittleEndian(_writeBuffer.AsSpan(2, 2), (ushort)resourceType);
        BinaryPrimitives.WriteUInt16LittleEndian(_writeBuffer.AsSpan(4, 2), (ushort)entries.Count);
        _stream.Write(_writeBuffer, 0, 6);

        // Calculate data offsets
        uint dataOffset = (uint)(6 + 16 * entries.Count);

        // Write ICONDIRENTRY structs
        foreach (var entry in entries)
        {
            // Width/height byte: 0 means 256 or more
            byte widthByte = entry.Width > 255 ? (byte)0 : (byte)entry.Width;
            byte heightByte = entry.Height > 255 ? (byte)0 : (byte)entry.Height;

            _writeBuffer[0] = widthByte;
            _writeBuffer[1] = heightByte;
            _writeBuffer[2] = entry.NumColors;
            _writeBuffer[3] = 0; // reserved
            BinaryPrimitives.WriteUInt16LittleEndian(_writeBuffer.AsSpan(4, 2), entry.ColorPlanesOrHotspotX);
            BinaryPrimitives.WriteUInt16LittleEndian(_writeBuffer.AsSpan(6, 2), entry.BitsPerPixelOrHotspotY);
            BinaryPrimitives.WriteUInt32LittleEndian(_writeBuffer.AsSpan(8, 4), (uint)entry.Data.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(_writeBuffer.AsSpan(12, 4), dataOffset);
            _stream.Write(_writeBuffer, 0, 16);

            dataOffset += (uint)entry.Data.Length;
        }

        // Write image data
        foreach (var entry in entries)
        {
            _stream.Write(entry.Data, 0, entry.Data.Length);
        }
    }

    /// <summary>
    /// Encodes a single image to an ICO directory entry.
    /// Automatically chooses BMP or PNG based on image characteristics.
    /// </summary>
    private IcoDirectoryEntry EncodeImage(
        IcoResourceType resourceType,
        int width,
        int height,
        byte[] rgba,
        (ushort x, ushort y)? hotspot)
    {
        var stats = IcoImageStats.Compute(rgba);

        // Heuristic: Use PNG for images with non-binary alpha or large images
        // PNG provides better compression for these cases
        bool usePng = stats.HasNonBinaryAlpha || width * height > 64 * 64;

        if (usePng)
        {
            return EncodePng(resourceType, width, height, rgba, stats, hotspot);
        }
        else
        {
            return EncodeBmp(resourceType, width, height, rgba, stats, hotspot);
        }
    }

    /// <summary>
    /// Encodes an image as PNG in an ICO directory entry.
    /// </summary>
    private IcoDirectoryEntry EncodePng(
        IcoResourceType resourceType,
        int width,
        int height,
        byte[] rgba,
        IcoImageStats stats,
        (ushort x, ushort y)? hotspot)
    {
        using var pngStream = new MemoryStream();
        WritePng(pngStream, width, height, rgba, stats.HasAlpha);
        byte[] pngData = pngStream.ToArray();

        ushort bitsPerPixel = (ushort)(stats.HasAlpha ? 32 : 24);

        var entry = new IcoDirectoryEntry
        {
            ResourceType = resourceType,
            Width = (uint)width,
            Height = (uint)height,
            NumColors = 0,
            ColorPlanesOrHotspotX = hotspot.HasValue ? hotspot.Value.x : (ushort)0,
            BitsPerPixelOrHotspotY = hotspot.HasValue ? hotspot.Value.y : bitsPerPixel,
            Data = pngData
        };

        return entry;
    }

    /// <summary>
    /// Encodes an image as BMP in an ICO directory entry.
    /// </summary>
    private IcoDirectoryEntry EncodeBmp(
        IcoResourceType resourceType,
        int width,
        int height,
        byte[] rgba,
        IcoImageStats stats,
        (ushort x, ushort y)? hotspot)
    {
        // Determine the most appropriate color depth
        IcoBmpDepth depth;
        List<(byte r, byte g, byte b)> colors;

        if (stats.HasNonBinaryAlpha)
        {
            // Only 32 bpp can support non-binary alpha
            depth = IcoBmpDepth.ThirtyTwo;
            colors = new List<(byte r, byte g, byte b)>();
        }
        else if (stats.Colors != null)
        {
            if (stats.Colors.Count <= 2)
            {
                depth = IcoBmpDepth.One;
                colors = stats.Colors.ToList();
            }
            else if (stats.Colors.Count <= 16)
            {
                depth = IcoBmpDepth.Four;
                colors = stats.Colors.ToList();
            }
            else if (stats.Colors.Count <= 256)
            {
                // For small images, 24bpp may be more efficient (no color table)
                if (width * height < 512)
                {
                    depth = IcoBmpDepth.TwentyFour;
                    colors = new List<(byte r, byte g, byte b)>();
                }
                else
                {
                    depth = IcoBmpDepth.Eight;
                    colors = stats.Colors.ToList();
                }
            }
            else
            {
                depth = IcoBmpDepth.TwentyFour;
                colors = new List<(byte r, byte g, byte b)>();
            }
        }
        else
        {
            depth = IcoBmpDepth.TwentyFour;
            colors = new List<(byte r, byte g, byte b)>();
        }

        ushort bitsPerPixel = (ushort)depth;
        int numColors = depth.GetNumColors();

        // Build color map
        var colorMap = new Dictionary<(byte r, byte g, byte b), byte>();
        for (int i = 0; i < colors.Count; i++)
        {
            colorMap[colors[i]] = (byte)i;
        }

        // Calculate sizes
        int rgbRowDataSize = ((width * bitsPerPixel) + 7) / 8;
        int rgbRowSize = ((rgbRowDataSize + 3) / 4) * 4;
        int rgbRowPadding = rgbRowSize - rgbRowDataSize;

        int maskRowDataSize = (width + 7) / 8;
        int maskRowSize = ((maskRowDataSize + 3) / 4) * 4;
        int maskRowPadding = maskRowSize - maskRowDataSize;

        int dataSize = BmpHeaderLen + 4 * numColors + height * (rgbRowSize + maskRowSize);

        using var dataStream = new MemoryStream(dataSize);

        // Write BITMAPINFOHEADER
        var header = new byte[BmpHeaderLen];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), BmpHeaderLen);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4, 4), width);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8, 4), 2 * height); // double height for AND mask
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12, 2), 1); // planes
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(14, 2), bitsPerPixel);
        // Remaining fields are 0 (compression, image size, ppm, colors used, important)
        dataStream.Write(header, 0, header.Length);

        // Write color table
        for (int i = 0; i < colors.Count; i++)
        {
            var (r, g, b) = colors[i];
            dataStream.WriteByte(b);
            dataStream.WriteByte(g);
            dataStream.WriteByte(r);
            dataStream.WriteByte(0); // reserved
        }
        // Pad color table to full size
        for (int i = colors.Count; i < numColors; i++)
        {
            dataStream.WriteByte(0);
            dataStream.WriteByte(0);
            dataStream.WriteByte(0);
            dataStream.WriteByte(0);
        }

        // Write color data (bottom-up)
        byte[] rowPaddingBytes = new byte[Math.Max(rgbRowPadding, maskRowPadding)];

        for (int row = 0; row < height; row++)
        {
            int srcRowStart = (height - row - 1) * width * 4;

            switch (depth)
            {
                case IcoBmpDepth.One:
                    WriteRow1Bpp(dataStream, rgba, srcRowStart, width, colorMap);
                    break;
                case IcoBmpDepth.Four:
                    WriteRow4Bpp(dataStream, rgba, srcRowStart, width, colorMap);
                    break;
                case IcoBmpDepth.Eight:
                    WriteRow8Bpp(dataStream, rgba, srcRowStart, width, colorMap);
                    break;
                case IcoBmpDepth.TwentyFour:
                    WriteRow24Bpp(dataStream, rgba, srcRowStart, width);
                    break;
                case IcoBmpDepth.ThirtyTwo:
                    WriteRow32Bpp(dataStream, rgba, srcRowStart, width);
                    break;
            }

            dataStream.Write(rowPaddingBytes, 0, rgbRowPadding);
        }

        // Write AND mask (1 = transparent)
        for (int row = 0; row < height; row++)
        {
            int srcRowStart = (height - row - 1) * width * 4;
            int col = 0;

            for (int i = 0; i < maskRowDataSize; i++)
            {
                byte maskByte = 0;
                for (int bit = 0; bit < 8 && col < width; bit++)
                {
                    // AND mask: 1 = transparent (alpha == 0)
                    if (rgba[srcRowStart + 3] == 0)
                    {
                        maskByte |= (byte)(1 << (7 - bit));
                    }
                    col++;
                    srcRowStart += 4;
                }
                dataStream.WriteByte(maskByte);
            }

            dataStream.Write(rowPaddingBytes, 0, maskRowPadding);
        }

        byte[] bmpData = dataStream.ToArray();

        var entry = new IcoDirectoryEntry
        {
            ResourceType = resourceType,
            Width = (uint)width,
            Height = (uint)height,
            NumColors = (byte)numColors,
            ColorPlanesOrHotspotX = hotspot.HasValue ? hotspot.Value.x : (ushort)1,
            BitsPerPixelOrHotspotY = hotspot.HasValue ? hotspot.Value.y : bitsPerPixel,
            Data = bmpData
        };

        return entry;
    }

    private void WriteRow1Bpp(MemoryStream stream, byte[] rgba, int srcOffset, int width, Dictionary<(byte r, byte g, byte b), byte> colorMap)
    {
        int col = 0;
        int rowDataSize = (width + 7) / 8;

        for (int i = 0; i < rowDataSize; i++)
        {
            byte b = 0;
            for (int bit = 0; bit < 8 && col < width; bit++)
            {
                var color = (rgba[srcOffset], rgba[srcOffset + 1], rgba[srcOffset + 2]);
                byte index = colorMap.TryGetValue(color, out var idx) ? idx : (byte)0;
                b |= (byte)((index & 0x1) << (7 - bit));
                col++;
                srcOffset += 4;
            }
            stream.WriteByte(b);
        }
    }

    private void WriteRow4Bpp(MemoryStream stream, byte[] rgba, int srcOffset, int width, Dictionary<(byte r, byte g, byte b), byte> colorMap)
    {
        int col = 0;
        int rowDataSize = (width + 1) / 2;

        for (int i = 0; i < rowDataSize; i++)
        {
            byte b = 0;
            for (int nibble = 0; nibble < 2 && col < width; nibble++)
            {
                var color = (rgba[srcOffset], rgba[srcOffset + 1], rgba[srcOffset + 2]);
                byte index = colorMap.TryGetValue(color, out var idx) ? idx : (byte)0;
                b |= (byte)((index & 0xF) << (4 * (1 - nibble)));
                col++;
                srcOffset += 4;
            }
            stream.WriteByte(b);
        }
    }

    private void WriteRow8Bpp(MemoryStream stream, byte[] rgba, int srcOffset, int width, Dictionary<(byte r, byte g, byte b), byte> colorMap)
    {
        for (int i = 0; i < width; i++)
        {
            var color = (rgba[srcOffset], rgba[srcOffset + 1], rgba[srcOffset + 2]);
            byte index = colorMap.TryGetValue(color, out var idx) ? idx : (byte)0;
            stream.WriteByte(index);
            srcOffset += 4;
        }
    }

    private void WriteRow24Bpp(MemoryStream stream, byte[] rgba, int srcOffset, int width)
    {
        for (int i = 0; i < width; i++)
        {
            stream.WriteByte(rgba[srcOffset + 2]); // B
            stream.WriteByte(rgba[srcOffset + 1]); // G
            stream.WriteByte(rgba[srcOffset]);     // R
            srcOffset += 4;
        }
    }

    private void WriteRow32Bpp(MemoryStream stream, byte[] rgba, int srcOffset, int width)
    {
        for (int i = 0; i < width; i++)
        {
            stream.WriteByte(rgba[srcOffset + 2]); // B
            stream.WriteByte(rgba[srcOffset + 1]); // G
            stream.WriteByte(rgba[srcOffset]);     // R
            stream.WriteByte(rgba[srcOffset + 3]); // A
            srcOffset += 4;
        }
    }

    /// <summary>
    /// Writes PNG data for an image.
    /// </summary>
    private void WritePng(Stream output, int width, int height, byte[] rgba, bool hasAlpha)
    {
        // PNG signature
        byte[] signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        output.Write(signature, 0, signature.Length);

        int bytesPerPixel = hasAlpha ? 4 : 3;

        // Build raw data with filter byte per row
        int rawDataLength = height * (1 + width * bytesPerPixel);
        var rawData = new byte[rawDataLength];
        int rawIndex = 0;

        for (int y = 0; y < height; y++)
        {
            rawData[rawIndex++] = 0; // No filter

            for (int x = 0; x < width; x++)
            {
                int srcOffset = (y * width + x) * 4;
                rawData[rawIndex++] = rgba[srcOffset];     // R
                rawData[rawIndex++] = rgba[srcOffset + 1]; // G
                rawData[rawIndex++] = rgba[srcOffset + 2]; // B
                if (hasAlpha)
                    rawData[rawIndex++] = rgba[srcOffset + 3]; // A
            }
        }

        // IHDR chunk
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0, 4), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4, 4), (uint)height);
        ihdr[8] = 8; // bit depth
        ihdr[9] = (byte)(hasAlpha ? 6 : 2); // color type: RGBA or RGB
        ihdr[10] = 0; // compression method
        ihdr[11] = 0; // filter method
        ihdr[12] = 0; // interlace method
        WriteChunk(output, "IHDR", ihdr);

        // IDAT chunk (compressed data)
        var compressedData = CompressDeflate(rawData);
        WriteChunk(output, "IDAT", compressedData);

        // IEND chunk
        WriteChunk(output, "IEND", Array.Empty<byte>());
    }

    private byte[] CompressDeflate(byte[] data)
    {
        const byte deflate32KbWindow = 120;
        const byte checksumBits = 1;

        using var compressStream = new MemoryStream();
        using (var deflate = new DeflateStream(compressStream, CompressionLevel.Optimal, true))
        {
            deflate.Write(data, 0, data.Length);
        }

        compressStream.Seek(0, SeekOrigin.Begin);

        var result = new byte[2 + compressStream.Length + 4];
        result[0] = deflate32KbWindow;
        result[1] = checksumBits;

        compressStream.Read(result, 2, (int)compressStream.Length);

        // Adler-32 checksum
        uint adler = ComputeAdler32(data);
        int offset = 2 + (int)compressStream.Length;
        result[offset++] = (byte)(adler >> 24);
        result[offset++] = (byte)(adler >> 16);
        result[offset++] = (byte)(adler >> 8);
        result[offset] = (byte)adler;

        return result;
    }

    private uint ComputeAdler32(byte[] data)
    {
        const uint modAdler = 65521;
        uint a = 1, b = 0;

        foreach (byte d in data)
        {
            a = (a + d) % modAdler;
            b = (b + a) % modAdler;
        }

        return (b << 16) | a;
    }

    private void WriteChunk(Stream output, string type, byte[] data)
    {
        // Length
        var lengthBytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(lengthBytes, (uint)data.Length);
        output.Write(lengthBytes, 0, 4);

        // Type
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes, 0, 4);

        // Data
        if (data.Length > 0)
            output.Write(data, 0, data.Length);

        // CRC
        uint crc = ComputePngCrc(typeBytes, data);
        var crcBytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes, 0, 4);
    }

    private uint ComputePngCrc(byte[] type, byte[] data)
    {
        uint crc = 0xFFFFFFFF;

        foreach (byte b in type)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);

        foreach (byte b in data)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);

        return ~crc;
    }

    private static readonly uint[] CrcTable = GenerateCrcTable();

    private static uint[] GenerateCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                if ((c & 1) != 0)
                    c = 0xEDB88320 ^ (c >> 1);
                else
                    c >>= 1;
            }
            table[n] = c;
        }
        return table;
    }
}
