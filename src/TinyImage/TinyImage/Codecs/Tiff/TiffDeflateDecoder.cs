using System;
using System.IO;
using System.IO.Compression;

namespace TinyImage.Codecs.Tiff;

/// <summary>
/// Deflate/Zlib decompression for TIFF images.
/// Based on UTIF.js deflate handling.
/// </summary>
internal static class TiffDeflateDecoder
{
    /// <summary>
    /// Decodes Deflate/Zlib compressed data.
    /// </summary>
    /// <param name="compressedData">The compressed data (with or without zlib header).</param>
    /// <param name="expectedSize">The expected uncompressed size.</param>
    /// <returns>The decompressed data.</returns>
    public static byte[] Decode(byte[] compressedData, int expectedSize)
    {
        if (compressedData == null || compressedData.Length == 0)
            return new byte[expectedSize];

        // Check for zlib header and skip if present
        // Zlib header: first byte is usually 0x78 (CMF), second byte varies (FLG)
        // Common combinations: 0x78 0x01, 0x78 0x5E, 0x78 0x9C, 0x78 0xDA
        int offset = 0;
        if (compressedData.Length >= 2)
        {
            byte cmf = compressedData[0];
            byte flg = compressedData[1];

            // Check if this looks like a zlib header
            // CMF = 0x78 means deflate with 32K window
            // Also verify checksum: (CMF * 256 + FLG) % 31 == 0
            if (cmf == 0x78 && ((cmf * 256 + flg) % 31 == 0))
            {
                offset = 2;
            }
        }

        try
        {
            using var inputStream = new MemoryStream(compressedData, offset, compressedData.Length - offset);
            using var deflateStream = new DeflateStream(inputStream, CompressionMode.Decompress);
            using var outputStream = new MemoryStream(expectedSize);

            deflateStream.CopyTo(outputStream);
            var result = outputStream.ToArray();

            // If result is smaller than expected, pad with zeros
            if (result.Length < expectedSize)
            {
                var padded = new byte[expectedSize];
                Buffer.BlockCopy(result, 0, padded, 0, result.Length);
                return padded;
            }

            return result;
        }
        catch (InvalidDataException)
        {
            // If decompression fails, try without skipping header
            if (offset > 0)
            {
                return DecodeRaw(compressedData, expectedSize);
            }
            throw;
        }
    }

    private static byte[] DecodeRaw(byte[] compressedData, int expectedSize)
    {
        using var inputStream = new MemoryStream(compressedData);
        using var deflateStream = new DeflateStream(inputStream, CompressionMode.Decompress);
        using var outputStream = new MemoryStream(expectedSize);

        deflateStream.CopyTo(outputStream);
        var result = outputStream.ToArray();

        if (result.Length < expectedSize)
        {
            var padded = new byte[expectedSize];
            Buffer.BlockCopy(result, 0, padded, 0, result.Length);
            return padded;
        }

        return result;
    }
}
