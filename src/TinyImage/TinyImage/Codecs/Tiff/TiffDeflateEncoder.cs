using System;
using System.IO;
using System.IO.Compression;

namespace TinyImage.Codecs.Tiff;

/// <summary>
/// Deflate compression for TIFF images.
/// </summary>
internal static class TiffDeflateEncoder
{
    /// <summary>
    /// Compresses data using Deflate algorithm with zlib header.
    /// </summary>
    public static byte[] Encode(byte[] data)
    {
        if (data == null || data.Length == 0)
            return Array.Empty<byte>();

        using var outputStream = new MemoryStream();

        // Write zlib header (deflate with 32K window)
        // CMF = 0x78 (deflate, 32K window)
        // FLG = 0x9C (default compression, no dict, checksum makes (CMF*256+FLG) % 31 == 0)
        outputStream.WriteByte(0x78);
        outputStream.WriteByte(0x9C);

        using (var deflateStream = new DeflateStream(outputStream, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflateStream.Write(data, 0, data.Length);
        }

        // Calculate Adler-32 checksum and append
        uint adler = ComputeAdler32(data);
        outputStream.WriteByte((byte)(adler >> 24));
        outputStream.WriteByte((byte)(adler >> 16));
        outputStream.WriteByte((byte)(adler >> 8));
        outputStream.WriteByte((byte)adler);

        return outputStream.ToArray();
    }

    /// <summary>
    /// Computes Adler-32 checksum.
    /// </summary>
    private static uint ComputeAdler32(byte[] data)
    {
        const uint Modulo = 65521;
        uint a = 1, b = 0;

        foreach (byte d in data)
        {
            a = (a + d) % Modulo;
            b = (b + a) % Modulo;
        }

        return (b << 16) | a;
    }
}
