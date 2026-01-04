using System;
using System.Collections.Generic;

namespace TinyImage.Codecs.Tiff;

/// <summary>
/// PackBits decompression for TIFF images.
/// Based on image-tiff decoder/stream.rs PackBitsReader.
/// </summary>
internal static class TiffPackBitsDecoder
{
    /// <summary>
    /// Decodes PackBits compressed data.
    /// </summary>
    /// <param name="compressedData">The compressed data.</param>
    /// <param name="expectedSize">The expected uncompressed size.</param>
    /// <returns>The decompressed data.</returns>
    public static byte[] Decode(byte[] compressedData, int expectedSize)
    {
        if (compressedData == null || compressedData.Length == 0)
            return new byte[expectedSize];

        var output = new List<byte>(expectedSize);
        int inputIndex = 0;

        while (inputIndex < compressedData.Length && output.Count < expectedSize)
        {
            // Read header byte (as signed byte)
            sbyte header = (sbyte)compressedData[inputIndex++];

            if (header >= 0)
            {
                // 0 <= n <= 127: copy next n+1 bytes literally
                int count = header + 1;

                for (int i = 0; i < count && inputIndex < compressedData.Length && output.Count < expectedSize; i++)
                {
                    output.Add(compressedData[inputIndex++]);
                }
            }
            else if (header != -128)
            {
                // -127 <= n <= -1: repeat next byte (1-n) times
                // header is negative, so 1 - header gives us count
                int count = 1 - header;

                if (inputIndex < compressedData.Length)
                {
                    byte value = compressedData[inputIndex++];

                    for (int i = 0; i < count && output.Count < expectedSize; i++)
                    {
                        output.Add(value);
                    }
                }
            }
            // header == -128: no-op (skip)
        }

        // Pad with zeros if output is smaller than expected
        while (output.Count < expectedSize)
        {
            output.Add(0);
        }

        return output.ToArray();
    }
}
