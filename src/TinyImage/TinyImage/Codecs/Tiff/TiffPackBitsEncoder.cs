using System;
using System.Collections.Generic;

namespace TinyImage.Codecs.Tiff;

/// <summary>
/// PackBits compression for TIFF images.
/// Based on image-tiff encoder/compression/packbits.rs.
/// </summary>
internal static class TiffPackBitsEncoder
{
    private const int MinRepeat = 3;    // Minimum run to compress between differ blocks
    private const int MaxBytes = 128;   // Maximum bytes that can be encoded in a header byte

    /// <summary>
    /// Compresses data using PackBits algorithm.
    /// </summary>
    public static byte[] Encode(byte[] data)
    {
        if (data == null || data.Length == 0)
            return Array.Empty<byte>();

        var output = new List<byte>(data.Length);

        int srcIndex = 0;
        int srcCount = data.Length;
        bool inRun = false;
        int runIndex = 0;
        int bytesPending = 0;
        int pendingIndex = 0;
        byte currentByte;
        byte lastByte;

        // Prime with first byte
        lastByte = data[srcIndex++];
        bytesPending = 1;
        srcCount--;

        while (srcCount > 0)
        {
            srcCount--;
            currentByte = data[srcIndex++];
            bytesPending++;

            if (inRun)
            {
                if (currentByte != lastByte || bytesPending > MaxBytes)
                {
                    // End of run - output repeat code
                    output.Add(EncodeRepeat(bytesPending - 1));
                    output.Add(lastByte);

                    bytesPending = 1;
                    pendingIndex = srcIndex - 1;
                    runIndex = 0;
                    inRun = false;
                }
            }
            else if (bytesPending > MaxBytes)
            {
                // Max literal bytes - output what we have
                output.Add(EncodeLiteral(MaxBytes));
                for (int i = 0; i < MaxBytes; i++)
                {
                    output.Add(data[pendingIndex + i]);
                }

                pendingIndex += MaxBytes;
                bytesPending -= MaxBytes;
                runIndex = bytesPending - 1;
            }
            else if (currentByte == lastByte)
            {
                // Potential run
                if (bytesPending - runIndex >= MinRepeat || runIndex == 0)
                {
                    // This is a worthwhile run
                    if (runIndex != 0)
                    {
                        // Flush literal data first
                        output.Add(EncodeLiteral(runIndex));
                        for (int i = 0; i < runIndex; i++)
                        {
                            output.Add(data[pendingIndex + i]);
                        }
                    }
                    bytesPending -= runIndex;
                    inRun = true;
                }
            }
            else
            {
                // Different byte - run could start here
                runIndex = bytesPending - 1;
            }

            lastByte = currentByte;
        }

        // Output remainder
        if (inRun)
        {
            output.Add(EncodeRepeat(bytesPending));
            output.Add(lastByte);
        }
        else
        {
            output.Add(EncodeLiteral(bytesPending));
            for (int i = 0; i < bytesPending; i++)
            {
                output.Add(data[pendingIndex + i]);
            }
        }

        return output.ToArray();
    }

    /// <summary>
    /// Encodes a literal (different bytes) count.
    /// n bytes: header = n - 1
    /// </summary>
    private static byte EncodeLiteral(int count)
    {
        return (byte)(count - 1);
    }

    /// <summary>
    /// Encodes a repeat count.
    /// n repeats: header = -(n - 1) = 1 - n
    /// </summary>
    private static byte EncodeRepeat(int count)
    {
        return unchecked((byte)(1 - count));
    }
}
