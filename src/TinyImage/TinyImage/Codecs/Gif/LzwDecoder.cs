using System;
using System.Collections.Generic;

namespace TinyImage.Codecs.Gif;

/// <summary>
/// LZW decoder for GIF image data decompression.
/// Adapted from UniGif (MIT License).
/// </summary>
internal static class LzwDecoder
{
    /// <summary>
    /// Decodes LZW compressed GIF data.
    /// </summary>
    /// <param name="compressedData">The LZW compressed data.</param>
    /// <param name="lzwMinimumCodeSize">The LZW minimum code size from the GIF.</param>
    /// <param name="expectedSize">The expected size of the decoded data (width * height).</param>
    /// <returns>The decoded pixel indices.</returns>
    public static byte[] Decode(List<byte> compressedData, int lzwMinimumCodeSize, int expectedSize)
    {
        if (expectedSize <= 0)
            return Array.Empty<byte>();

        if (lzwMinimumCodeSize < 2)
            lzwMinimumCodeSize = 2;

        int clearCode = 1 << lzwMinimumCodeSize;
        int endCode = clearCode + 1;
        int nextCode = endCode + 1;
        int codeSize = lzwMinimumCodeSize + 1;
        const int codeSizeLimit = 12;

        // Dictionary: index -> byte sequence (max 4096 entries for 12-bit codes)
        var dictionary = new byte[4096][];
        for (int i = 0; i < clearCode; i++)
        {
            dictionary[i] = new byte[] { (byte)i };
        }
        dictionary[clearCode] = null!;
        dictionary[endCode] = null!;

        var output = new List<byte>(expectedSize);
        int bitPosition = 0;
        int compressedLength = compressedData.Count;
        byte[]? previous = null;

        while (output.Count < expectedSize)
        {
            // Read next code
            int code = ReadCode(compressedData, ref bitPosition, codeSize, compressedLength);
            if (code < 0)
                break;

            if (code == clearCode)
            {
                // Reset dictionary
                for (int i = 0; i < clearCode; i++)
                {
                    dictionary[i] = new byte[] { (byte)i };
                }
                nextCode = endCode + 1;
                codeSize = lzwMinimumCodeSize + 1;
                previous = null;
                continue;
            }

            if (code == endCode)
                break;

            byte[] entry;
            if (code < nextCode && dictionary[code] != null)
            {
                entry = dictionary[code];
            }
            else if (code == nextCode && previous != null)
            {
                // KwKwK case
                entry = new byte[previous.Length + 1];
                Buffer.BlockCopy(previous, 0, entry, 0, previous.Length);
                entry[entry.Length - 1] = previous[0];
            }
            else
            {
                // Malformed stream
                break;
            }

            // Output entry (up to expectedSize)
            int copyLength = Math.Min(entry.Length, expectedSize - output.Count);
            for (int i = 0; i < copyLength; i++)
            {
                output.Add(entry[i]);
            }

            if (copyLength < entry.Length)
                break;

            // Add new dictionary entry
            if (previous != null && nextCode < dictionary.Length)
            {
                var newEntry = new byte[previous.Length + 1];
                Buffer.BlockCopy(previous, 0, newEntry, 0, previous.Length);
                newEntry[newEntry.Length - 1] = entry[0];
                dictionary[nextCode] = newEntry;
                nextCode++;

                // Increase code size if needed
                if (nextCode == (1 << codeSize) && codeSize < codeSizeLimit)
                {
                    codeSize++;
                }
            }

            previous = entry;
        }

        // Pad with zeros if needed (for malformed GIFs)
        while (output.Count < expectedSize)
        {
            output.Add(0);
        }

        return output.ToArray();
    }

    /// <summary>
    /// Reads a variable-length code from the bit stream.
    /// </summary>
    private static int ReadCode(List<byte> data, ref int bitPosition, int codeSize, int dataLength)
    {
        int code = 0;
        int bitsRead = 0;

        while (bitsRead < codeSize)
        {
            int byteIndex = bitPosition >> 3;
            if (byteIndex >= dataLength)
                return -1;

            int bitIndex = bitPosition & 7;
            int bit = (data[byteIndex] >> bitIndex) & 1;
            code |= bit << bitsRead;

            bitPosition++;
            bitsRead++;
        }

        return code;
    }

    /// <summary>
    /// Sorts interlaced GIF data into proper scan order.
    /// </summary>
    /// <param name="decodedData">The decoded pixel data in interlaced order.</param>
    /// <param name="width">The image width.</param>
    /// <param name="height">The image height.</param>
    /// <returns>The data sorted into normal scan order.</returns>
    public static byte[] SortInterlacedData(byte[] decodedData, int width, int height)
    {
        var sorted = new byte[decodedData.Length];
        int sourceIndex = 0;

        // Pass 1: Every 8th row, starting at row 0
        for (int y = 0; y < height; y += 8)
        {
            int destOffset = y * width;
            for (int x = 0; x < width && sourceIndex < decodedData.Length; x++)
            {
                sorted[destOffset + x] = decodedData[sourceIndex++];
            }
        }

        // Pass 2: Every 8th row, starting at row 4
        for (int y = 4; y < height; y += 8)
        {
            int destOffset = y * width;
            for (int x = 0; x < width && sourceIndex < decodedData.Length; x++)
            {
                sorted[destOffset + x] = decodedData[sourceIndex++];
            }
        }

        // Pass 3: Every 4th row, starting at row 2
        for (int y = 2; y < height; y += 4)
        {
            int destOffset = y * width;
            for (int x = 0; x < width && sourceIndex < decodedData.Length; x++)
            {
                sorted[destOffset + x] = decodedData[sourceIndex++];
            }
        }

        // Pass 4: Every 2nd row, starting at row 1
        for (int y = 1; y < height; y += 2)
        {
            int destOffset = y * width;
            for (int x = 0; x < width && sourceIndex < decodedData.Length; x++)
            {
                sorted[destOffset + x] = decodedData[sourceIndex++];
            }
        }

        return sorted;
    }
}
