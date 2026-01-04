using System;
using System.Collections.Generic;

namespace TinyImage.Codecs.Tiff;

/// <summary>
/// Decoder for CCITT Group 4 (T.6) Fax compression.
/// Based on UTIF.js _decodeG4 implementation.
/// </summary>
internal static class TiffFax4Decoder
{
    /// <summary>
    /// Decodes Group 4 compressed data.
    /// </summary>
    /// <param name="data">Compressed data.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <returns>Decompressed bilevel data (1 bit per pixel, packed into bytes).</returns>
    public static byte[] Decode(byte[] data, int width, int height)
    {
        int bytesPerRow = (width + 7) / 8;
        var output = new byte[bytesPerRow * height];

        // Bit position in input data
        int bitPos = 0;

        // Current line and previous (reference) line as run-length transitions
        var line = new List<int>();
        var prevLine = new List<(int pos, int color)>();

        // Initialize reference line (all white)
        prevLine.Add((width, 0));
        prevLine.Add((width, 1));

        int a0 = 0;
        int color = 0; // 0 = white, 1 = black
        string word = "";
        string mode = "";
        int toRead = 0;
        int len = 0;
        int y = 0;

        var whiteCodes = TiffFaxTables.WhiteCodes;
        var blackCodes = TiffFaxTables.BlackCodes;
        var modeCodes = TiffFaxTables.ModeCodes;

        while (bitPos / 8 < data.Length && y < height)
        {
            // Find b1 and b2 reference positions
            int b1 = FindDiff(prevLine, a0 + (a0 == 0 ? 0 : 1), 1 - color);
            int b2 = FindDiff(prevLine, b1, color);

            // Read next bit
            int bit = GetBit(data, bitPos);
            bitPos++;
            word += (char)('0' + bit);

            if (mode == "H")
            {
                // Horizontal mode - decode run lengths
                var codes = color == 0 ? whiteCodes : blackCodes;
                if (codes.TryGetValue(word, out int dl))
                {
                    word = "";
                    len += dl;
                    if (dl < 64)
                    {
                        // Terminating code
                        AddRun(line, len, color);
                        a0 += len;
                        color = 1 - color;
                        len = 0;
                        toRead--;
                        if (toRead == 0) mode = "";
                    }
                }
            }
            else
            {
                // Check for mode codes
                if (word == TiffFaxTables.PassMode)
                {
                    // Pass mode: a0 moves to b2
                    word = "";
                    AddRun(line, b2 - a0, color);
                    a0 = b2;
                }
                else if (word == TiffFaxTables.HorizontalMode)
                {
                    // Horizontal mode
                    word = "";
                    mode = "H";
                    toRead = 2;
                }
                else if (modeCodes.TryGetValue(word, out int delta))
                {
                    // Vertical mode
                    int a1 = b1 + delta;
                    AddRun(line, a1 - a0, color);
                    a0 = a1;
                    word = "";
                    color = 1 - color;
                }
            }

            // Check if line is complete
            if (GetLineLength(line) >= width && mode == "")
            {
                // Write completed line to output
                WriteBits(line, output, y * bytesPerRow * 8, width);

                // Prepare for next line
                color = 0;
                y++;
                a0 = 0;
                prevLine = MakeDiff(line);
                line = new List<int>();
            }
        }

        return output;
    }

    /// <summary>
    /// Gets a single bit from the data at the specified bit position.
    /// </summary>
    private static int GetBit(byte[] data, int bitPos)
    {
        int byteIndex = bitPos / 8;
        if (byteIndex >= data.Length) return 0;

        // MSB first (fill order 1)
        int bitIndex = 7 - (bitPos % 8);
        return (data[byteIndex] >> bitIndex) & 1;
    }

    /// <summary>
    /// Finds the next transition position at or after x with the specified color.
    /// </summary>
    private static int FindDiff(List<(int pos, int color)> line, int x, int targetColor)
    {
        for (int i = 0; i < line.Count; i += 2)
        {
            if (i + 1 < line.Count && line[i].pos >= x && line[i + 1].color == targetColor)
            {
                return line[i].pos;
            }
        }
        // Return line end position from the last entry
        return line.Count > 0 ? line[line.Count - 1].pos : 0;
    }

    /// <summary>
    /// Converts a line of pixel values to run-length transition format.
    /// </summary>
    private static List<(int pos, int color)> MakeDiff(List<int> line)
    {
        var result = new List<(int pos, int color)>();
        if (line.Count == 0) return result;

        if (line[0] == 1)
        {
            result.Add((0, 1));
        }

        for (int i = 1; i < line.Count; i++)
        {
            if (line[i - 1] != line[i])
            {
                result.Add((i, line[i]));
            }
        }

        // Add end markers
        result.Add((line.Count, 0));
        result.Add((line.Count, 1));

        return result;
    }

    /// <summary>
    /// Adds a run of pixels with the specified color to the line.
    /// </summary>
    private static void AddRun(List<int> line, int length, int color)
    {
        for (int i = 0; i < length; i++)
        {
            line.Add(color);
        }
    }

    /// <summary>
    /// Gets the current line length in pixels.
    /// </summary>
    private static int GetLineLength(List<int> line)
    {
        return line.Count;
    }

    /// <summary>
    /// Writes pixel bits to the output buffer.
    /// </summary>
    private static void WriteBits(List<int> bits, byte[] target, int bitOffset, int width)
    {
        int count = Math.Min(bits.Count, width);
        for (int i = 0; i < count; i++)
        {
            if (bits[i] == 1)
            {
                int pos = bitOffset + i;
                target[pos / 8] |= (byte)(1 << (7 - (pos % 8)));
            }
        }
    }
}
