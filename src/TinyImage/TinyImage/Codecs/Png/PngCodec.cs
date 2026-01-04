using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace TinyImage.Codecs.Png;

/// <summary>
/// PNG codec for encoding and decoding PNG images.
/// Based on BigGustave (Unlicense).
/// </summary>
internal static class PngCodec
{
    private const byte Deflate32KbWindow = 120;
    private const byte ChecksumBits = 1;

    /// <summary>
    /// Decodes a PNG image from a stream.
    /// </summary>
    public static Image Decode(Stream stream)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));
        if (!stream.CanRead)
            throw new ArgumentException("The provided stream is not readable.");

        var validHeader = HasValidHeader(stream);
        if (!validHeader.IsValid)
            throw new ArgumentException("The provided stream did not start with the PNG header.");

        var crc = new byte[4];
        var imageHeader = ReadImageHeader(stream, crc);
        var hasEncounteredImageEnd = false;
        PngPalette? palette = null;

        using var output = new MemoryStream();
        using var memoryStream = new MemoryStream();

        while (TryReadChunkHeader(stream, out var header))
        {
            if (hasEncounteredImageEnd)
                break;

            var bytes = new byte[header.Length];
            var read = stream.Read(bytes, 0, bytes.Length);
            if (read != bytes.Length)
                throw new InvalidOperationException($"Did not read {header.Length} bytes for the {header.Name} header.");

            if (header.IsCritical)
            {
                switch (header.Name)
                {
                    case "PLTE":
                        if (header.Length % 3 != 0)
                            throw new InvalidOperationException($"Palette data must be multiple of 3, got {header.Length}.");
                        if (imageHeader.ColorType.HasFlag(PngColorType.PaletteUsed))
                            palette = new PngPalette(bytes);
                        break;
                    case "IDAT":
                        memoryStream.Write(bytes, 0, bytes.Length);
                        break;
                    case "IEND":
                        hasEncounteredImageEnd = true;
                        break;
                    default:
                        throw new NotSupportedException($"Encountered critical header {header.Name} which was not recognised.");
                }
            }
            else
            {
                if (header.Name == "tRNS" && palette != null)
                    palette.SetAlphaValues(bytes);
            }

            read = stream.Read(crc, 0, crc.Length);
            if (read != 4)
                throw new InvalidOperationException($"Did not read 4 bytes for the CRC.");

            var result = (int)PngCrc32.Calculate(Encoding.ASCII.GetBytes(header.Name), bytes);
            var crcActual = (crc[0] << 24) + (crc[1] << 16) + (crc[2] << 8) + crc[3];

            if (result != crcActual)
                throw new InvalidOperationException($"CRC calculated {result} did not match file {crcActual} for chunk: {header.Name}.");
        }

        memoryStream.Flush();
        memoryStream.Seek(2, SeekOrigin.Begin);

        using (var deflateStream = new DeflateStream(memoryStream, CompressionMode.Decompress))
        {
            deflateStream.CopyTo(output);
        }

        var bytesOut = output.ToArray();
        var (bytesPerPixel, samplesPerPixel) = PngDecoder.GetBytesAndSamplesPerPixel(imageHeader);
        bytesOut = PngDecoder.Decode(bytesOut, imageHeader, bytesPerPixel, samplesPerPixel);

        var rawData = new PngRawData(bytesOut, bytesPerPixel, palette, imageHeader);
        var hasAlpha = (imageHeader.ColorType & PngColorType.AlphaChannelUsed) != 0 || (palette?.HasAlphaValues ?? false);

        return ConvertToImage(rawData, imageHeader.Width, imageHeader.Height, hasAlpha);
    }

    /// <summary>
    /// Encodes an image to PNG format.
    /// </summary>
    public static void Encode(Image image, Stream stream)
    {
        if (image == null)
            throw new ArgumentNullException(nameof(image));
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        var width = image.Width;
        var height = image.Height;
        var hasAlpha = image.HasAlpha;
        var bytesPerPixel = hasAlpha ? 4 : 3;

        // Build raw data with filter byte per row
        var rawDataLength = (height * width * bytesPerPixel) + height;
        var rawData = new byte[rawDataLength];

        var buffer = image.GetBuffer();
        var rawIndex = 0;

        for (var y = 0; y < height; y++)
        {
            rawData[rawIndex++] = 0; // None filter

            for (var x = 0; x < width; x++)
            {
                var pixel = buffer.GetPixel(x, y);
                rawData[rawIndex++] = pixel.R;
                rawData[rawIndex++] = pixel.G;
                rawData[rawIndex++] = pixel.B;
                if (hasAlpha)
                    rawData[rawIndex++] = pixel.A;
            }
        }

        // Write PNG
        stream.Write(PngHeaderValidation.ExpectedHeader, 0, PngHeaderValidation.ExpectedHeader.Length);

        var writer = new PngStreamWriteHelper(stream);

        // IHDR
        writer.WriteChunkLength(13);
        writer.WriteChunkHeader(PngImageHeader.HeaderBytes);
        PngStreamHelper.WriteBigEndianInt32(writer, width);
        PngStreamHelper.WriteBigEndianInt32(writer, height);
        writer.WriteByte(8); // bit depth
        writer.WriteByte((byte)(hasAlpha ? (PngColorType.ColorUsed | PngColorType.AlphaChannelUsed) : PngColorType.ColorUsed));
        writer.WriteByte((byte)PngCompressionMethod.DeflateWithSlidingWindow);
        writer.WriteByte((byte)PngFilterMethod.AdaptiveFiltering);
        writer.WriteByte((byte)PngInterlaceMethod.None);
        writer.WriteCrc();

        // IDAT
        var imageData = Compress(rawData, rawDataLength);
        writer.WriteChunkLength(imageData.Length);
        writer.WriteChunkHeader(Encoding.ASCII.GetBytes("IDAT"));
        writer.Write(imageData, 0, imageData.Length);
        writer.WriteCrc();

        // IEND
        writer.WriteChunkLength(0);
        writer.WriteChunkHeader(Encoding.ASCII.GetBytes("IEND"));
        writer.WriteCrc();
    }

    private static byte[] Compress(byte[] data, int dataLength)
    {
        const int headerLength = 2;
        const int checksumLength = 4;

        using var compressStream = new MemoryStream();
        using (var compressor = new DeflateStream(compressStream, CompressionLevel.Optimal, true))
        {
            compressor.Write(data, 0, dataLength);
        }

        compressStream.Seek(0, SeekOrigin.Begin);

        var result = new byte[headerLength + compressStream.Length + checksumLength];
        result[0] = Deflate32KbWindow;
        result[1] = ChecksumBits;

        int streamValue;
        var i = 0;
        while ((streamValue = compressStream.ReadByte()) != -1)
        {
            result[headerLength + i] = (byte)streamValue;
            i++;
        }

        var checksum = PngAdler32.Calculate(data, dataLength);
        var offset = headerLength + compressStream.Length;
        result[offset++] = (byte)(checksum >> 24);
        result[offset++] = (byte)(checksum >> 16);
        result[offset++] = (byte)(checksum >> 8);
        result[offset] = (byte)checksum;

        return result;
    }

    private static Image ConvertToImage(PngRawData rawData, int width, int height, bool hasAlpha)
    {
        var buffer = new PixelBuffer(width, height);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                buffer.SetPixel(x, y, rawData.GetPixel(x, y));
            }
        }

        return new Image(buffer, hasAlpha);
    }

    private static PngHeaderValidation HasValidHeader(Stream stream)
    {
        return new PngHeaderValidation(
            stream.ReadByte(), stream.ReadByte(), stream.ReadByte(), stream.ReadByte(),
            stream.ReadByte(), stream.ReadByte(), stream.ReadByte(), stream.ReadByte());
    }

    private static bool TryReadChunkHeader(Stream stream, out PngChunkHeader chunkHeader)
    {
        chunkHeader = default;
        var position = stream.Position;

        if (!PngStreamHelper.TryReadHeaderBytes(stream, out var headerBytes))
            return false;

        var length = PngStreamHelper.ReadBigEndianInt32(headerBytes, 0);
        var name = Encoding.ASCII.GetString(headerBytes, 4, 4);

        chunkHeader = new PngChunkHeader(position, length, name);
        return true;
    }

    private static PngImageHeader ReadImageHeader(Stream stream, byte[] crc)
    {
        if (!TryReadChunkHeader(stream, out var header))
            throw new ArgumentException("The provided stream did not contain a single chunk.");

        if (header.Name != "IHDR")
            throw new ArgumentException($"The first chunk was not the IHDR chunk: {header.Name}.");

        if (header.Length != 13)
            throw new ArgumentException($"The first chunk did not have a length of 13 bytes.");

        var ihdrBytes = new byte[13];
        var read = stream.Read(ihdrBytes, 0, ihdrBytes.Length);

        if (read != 13)
            throw new InvalidOperationException($"Did not read 13 bytes for the IHDR.");

        read = stream.Read(crc, 0, crc.Length);
        if (read != 4)
            throw new InvalidOperationException($"Did not read 4 bytes for the CRC.");

        var width = PngStreamHelper.ReadBigEndianInt32(ihdrBytes, 0);
        var height = PngStreamHelper.ReadBigEndianInt32(ihdrBytes, 4);
        var bitDepth = ihdrBytes[8];
        var colorType = ihdrBytes[9];
        var compressionMethod = ihdrBytes[10];
        var filterMethod = ihdrBytes[11];
        var interlaceMethod = ihdrBytes[12];

        return new PngImageHeader(width, height, bitDepth, (PngColorType)colorType,
            (PngCompressionMethod)compressionMethod, (PngFilterMethod)filterMethod, (PngInterlaceMethod)interlaceMethod);
    }
}
