using System;
using System.Collections.Generic;
using System.IO;
using TinyImage.Codecs.WebP.Core;

namespace TinyImage.Codecs.WebP.Lossless;

/// <summary>
/// VP8L lossless WebP encoder.
/// Translated from webp-rust encoder.rs
/// </summary>
internal class VP8LEncoder
{
    private readonly BitWriter _bitWriter;
    private readonly bool _usePredictorTransform;

    public VP8LEncoder(Stream stream, bool usePredictorTransform = true)
    {
        _bitWriter = new BitWriter(stream);
        _usePredictorTransform = usePredictorTransform;
    }

    /// <summary>
    /// Encodes RGBA pixel data to VP8L format.
    /// </summary>
    public void Encode(byte[] rgba, int width, int height, bool hasAlpha, bool implicitDimensions = false)
    {
        if (width <= 0 || width > 16384 || height <= 0 || height > 16384)
            throw new WebPEncodingException("Invalid image dimensions");

        if (rgba.Length != width * height * 4)
            throw new WebPEncodingException("RGBA data size mismatch");

        // Write header if not implicit
        if (!implicitDimensions)
        {
            _bitWriter.WriteBits(0x2f, 8); // VP8L signature
            _bitWriter.WriteBits((ulong)(width - 1), 14);
            _bitWriter.WriteBits((ulong)(height - 1), 14);
            _bitWriter.WriteBits(hasAlpha ? 1UL : 0UL, 1); // alpha used
            _bitWriter.WriteBits(0, 3); // version
        }

        // Apply subtract green transform (always)
        _bitWriter.WriteBits(0b101, 3); // transform flag + subtract green

        // Apply predictor transform if enabled
        if (_usePredictorTransform)
        {
            _bitWriter.WriteBits(0b111001, 6); // transform flag + predictor + size bits
            _bitWriter.WriteBits(0, 1); // no color cache for predictor
            _bitWriter.WriteSingleEntryHuffmanTree(2); // predictor mode 2 (vertical)
            for (int i = 0; i < 4; i++)
                _bitWriter.WriteSingleEntryHuffmanTree(0); // R, B, A, distance
        }

        // No more transforms
        _bitWriter.WriteBits(0, 1);

        // No color cache
        _bitWriter.WriteBits(0, 1);

        // No meta-huffman codes
        _bitWriter.WriteBits(0, 1);

        // Transform the pixel data
        byte[] pixels = new byte[rgba.Length];
        Array.Copy(rgba, pixels, rgba.Length);

        // Apply subtract green transform
        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte g = pixels[i + 1];
            pixels[i] = (byte)(pixels[i] - g);     // R -= G
            pixels[i + 2] = (byte)(pixels[i + 2] - g); // B -= G
        }

        // Apply predictor transform
        if (_usePredictorTransform)
        {
            int rowBytes = width * 4;
            // Process from bottom to top (reverse order)
            for (int y = height - 1; y > 0; y--)
            {
                int currentRow = y * rowBytes;
                int prevRow = (y - 1) * rowBytes;
                for (int x = 0; x < rowBytes; x++)
                {
                    pixels[currentRow + x] = (byte)(pixels[currentRow + x] - pixels[prevRow + x]);
                }
            }
            // First row: subtract from left neighbor
            for (int i = rowBytes - 1; i >= 4; i--)
            {
                pixels[i] = (byte)(pixels[i] - pixels[i - 4]);
            }
            // First pixel: subtract 255 from alpha
            pixels[3] = (byte)(pixels[3] - 255);
        }

        // Compute frequencies
        uint[] frequenciesG = new uint[280]; // Green + length symbols
        uint[] frequenciesR = new uint[256];
        uint[] frequenciesB = new uint[256];
        uint[] frequenciesA = new uint[256];

        int pixelCount = width * height;
        for (int i = 0; i < pixelCount; i++)
        {
            int idx = i * 4;
            frequenciesR[pixels[idx]]++;
            frequenciesG[pixels[idx + 1]]++;
            frequenciesB[pixels[idx + 2]]++;
            frequenciesA[pixels[idx + 3]]++;

            // Count runs
            CountRun(pixels, i, pixelCount, frequenciesG);
        }

        // Build and write Huffman trees
        byte[] lengthsG = new byte[280];
        ushort[] codesG = new ushort[280];
        byte[] lengthsR = new byte[256];
        ushort[] codesR = new ushort[256];
        byte[] lengthsB = new byte[256];
        ushort[] codesB = new ushort[256];
        byte[] lengthsA = new byte[256];
        ushort[] codesA = new ushort[256];

        WriteHuffmanTree(frequenciesG, lengthsG, codesG);
        WriteHuffmanTree(frequenciesR, lengthsR, codesR);
        WriteHuffmanTree(frequenciesB, lengthsB, codesB);

        if (hasAlpha)
        {
            WriteHuffmanTree(frequenciesA, lengthsA, codesA);
        }
        else if (_usePredictorTransform)
        {
            _bitWriter.WriteSingleEntryHuffmanTree(0);
        }
        else
        {
            _bitWriter.WriteSingleEntryHuffmanTree(255);
        }

        // Distance tree (single entry, code 1)
        _bitWriter.WriteSingleEntryHuffmanTree(1);

        // Write image data
        for (int i = 0; i < pixelCount;)
        {
            int idx = i * 4;
            byte r = pixels[idx];
            byte g = pixels[idx + 1];
            byte b = pixels[idx + 2];
            byte a = pixels[idx + 3];

            // Write pixel
            _bitWriter.WriteBits(codesG[g], lengthsG[g]);
            _bitWriter.WriteBits(codesR[r], lengthsR[r]);
            _bitWriter.WriteBits(codesB[b], lengthsB[b]);
            if (hasAlpha)
                _bitWriter.WriteBits(codesA[a], lengthsA[a]);

            i++;

            // Write runs
            int runLength = 0;
            while (runLength < 4096 && i + runLength < pixelCount)
            {
                int nextIdx = (i + runLength) * 4;
                if (pixels[nextIdx] != r || pixels[nextIdx + 1] != g ||
                    pixels[nextIdx + 2] != b || pixels[nextIdx + 3] != a)
                    break;
                runLength++;
            }

            if (runLength > 0)
            {
                WriteRun(runLength, codesG, lengthsG);
                i += runLength;
            }
        }

        _bitWriter.Flush();
    }

    private void CountRun(byte[] pixels, int startPixel, int totalPixels, uint[] frequencies)
    {
        int idx = startPixel * 4;
        byte r = pixels[idx];
        byte g = pixels[idx + 1];
        byte b = pixels[idx + 2];
        byte a = pixels[idx + 3];

        int runLength = 0;
        int i = startPixel + 1;
        while (runLength < 4096 && i < totalPixels)
        {
            int nextIdx = i * 4;
            if (pixels[nextIdx] != r || pixels[nextIdx + 1] != g ||
                pixels[nextIdx + 2] != b || pixels[nextIdx + 3] != a)
                break;
            runLength++;
            i++;
        }

        if (runLength > 0)
        {
            if (runLength <= 4)
            {
                frequencies[256 + runLength - 1]++;
            }
            else
            {
                var (symbol, _) = LengthToSymbol((ushort)runLength);
                frequencies[256 + symbol]++;
            }
        }
    }

    private void WriteRun(int runLength, ushort[] codes, byte[] lengths)
    {
        if (runLength <= 4)
        {
            int symbol = 256 + runLength - 1;
            _bitWriter.WriteBits(codes[symbol], lengths[symbol]);
        }
        else
        {
            var (symbol, extraBits) = LengthToSymbol((ushort)runLength);
            _bitWriter.WriteBits(codes[256 + symbol], lengths[256 + symbol]);
            _bitWriter.WriteBits((ulong)(runLength - 1) & ((1UL << extraBits) - 1), extraBits);
        }
    }

    private static (ushort symbol, byte extraBits) LengthToSymbol(ushort len)
    {
        int adjusted = len - 1;
        int highestBit = Log2(adjusted);
        int secondHighestBit = (adjusted >> (highestBit - 1)) & 1;
        int extraBits = highestBit - 1;
        int symbol = 2 * highestBit + secondHighestBit;
        return ((ushort)symbol, (byte)extraBits);
    }

    private static int Log2(int value)
    {
        int log = 0;
        while (value > 1)
        {
            value >>= 1;
            log++;
        }
        return log;
    }

    private void WriteHuffmanTree(uint[] frequencies, byte[] lengths, ushort[] codes)
    {
        if (!BuildHuffmanTree(frequencies, lengths, codes, 15))
        {
            // Single entry tree
            int symbol = 0;
            for (int i = 0; i < frequencies.Length; i++)
            {
                if (frequencies[i] > 0)
                {
                    symbol = i;
                    break;
                }
            }
            _bitWriter.WriteSingleEntryHuffmanTree((byte)symbol);
            return;
        }

        // Build code length frequencies and codes
        uint[] codeLengthFreq = new uint[16];
        foreach (byte len in lengths)
            codeLengthFreq[len]++;

        byte[] codeLengthLengths = new byte[16];
        ushort[] codeLengthCodes = new ushort[16];
        bool singleCodeLength = !BuildHuffmanTree(codeLengthFreq, codeLengthLengths, codeLengthCodes, 7);

        int[] codeLengthOrder = { 17, 18, 0, 1, 2, 3, 4, 5, 16, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 };

        // Write normal huffman tree header
        _bitWriter.WriteBits(0, 1); // normal huffman tree
        _bitWriter.WriteBits(19 - 4, 4); // num_code_lengths - 4

        // Write code lengths for the code length alphabet
        foreach (int i in codeLengthOrder)
        {
            if (i > 15 || codeLengthFreq[i] == 0)
                _bitWriter.WriteBits(0, 3);
            else if (singleCodeLength)
                _bitWriter.WriteBits(1, 3);
            else
                _bitWriter.WriteBits(codeLengthLengths[i], 3);
        }

        // Write max_symbol
        if (lengths.Length == 256)
        {
            _bitWriter.WriteBits(1, 1); // max_symbol is stored
            _bitWriter.WriteBits(3, 3); // max_symbol_nbits / 2 - 2
            _bitWriter.WriteBits(254, 8); // max_symbol - 2
        }
        else if (lengths.Length == 280)
        {
            _bitWriter.WriteBits(0, 1); // use default max_symbol
        }

        // Write the huffman codes
        if (!singleCodeLength)
        {
            foreach (byte len in lengths)
            {
                _bitWriter.WriteBits(codeLengthCodes[len], codeLengthLengths[len]);
            }
        }
    }

    private static bool BuildHuffmanTree(uint[] frequencies, byte[] lengths, ushort[] codes, byte lengthLimit)
    {
        // Count non-zero frequencies
        int nonZeroCount = 0;
        for (int i = 0; i < frequencies.Length; i++)
        {
            if (frequencies[i] > 0)
                nonZeroCount++;
        }

        if (nonZeroCount <= 1)
        {
            Array.Clear(lengths, 0, lengths.Length);
            Array.Clear(codes, 0, codes.Length);
            return false;
        }

        // Build huffman tree using priority queue
        var heap = new SortedSet<(uint freq, int index, bool isInternal, int left, int right)>(
            Comparer<(uint freq, int index, bool isInternal, int left, int right)>.Create((a, b) =>
            {
                int result = a.freq.CompareTo(b.freq);
                if (result == 0)
                    result = a.index.CompareTo(b.index);
                return result;
            }));

        int nextInternalIndex = frequencies.Length;
        for (int i = 0; i < frequencies.Length; i++)
        {
            if (frequencies[i] > 0)
                heap.Add((frequencies[i], i, false, -1, -1));
        }

        var internalNodes = new List<(int left, int right)>();

        while (heap.Count > 1)
        {
            var node1 = heap.Min;
            heap.Remove(node1);
            var node2 = heap.Min;
            heap.Remove(node2);

            internalNodes.Add((node1.index, node2.index));
            heap.Add((node1.freq + node2.freq, nextInternalIndex++, true, node1.index, node2.index));
        }

        // Assign code lengths by walking the tree
        Array.Clear(lengths, 0, lengths.Length);
        var stack = new Stack<(int node, int depth)>();

        if (heap.Count > 0)
        {
            var root = heap.Min;
            stack.Push((root.index, 0));
        }

        while (stack.Count > 0)
        {
            var (node, depth) = stack.Pop();

            if (node < frequencies.Length)
            {
                lengths[node] = (byte)depth;
            }
            else
            {
                int internalIndex = node - frequencies.Length;
                var (left, right) = internalNodes[internalIndex];
                stack.Push((left, depth + 1));
                stack.Push((right, depth + 1));
            }
        }

        // Limit code lengths
        int maxLength = 0;
        foreach (byte len in lengths)
            maxLength = Math.Max(maxLength, len);

        if (maxLength > lengthLimit)
        {
            LimitCodeLengths(frequencies, lengths, lengthLimit);
        }

        // Assign codes
        Array.Clear(codes, 0, codes.Length);
        uint code = 0;
        for (int len = 1; len <= lengthLimit; len++)
        {
            for (int i = 0; i < lengths.Length; i++)
            {
                if (lengths[i] == len)
                {
                    codes[i] = ReverseBits((ushort)code, len);
                    code++;
                }
            }
            code <<= 1;
        }

        return true;
    }

    private static void LimitCodeLengths(uint[] frequencies, byte[] lengths, byte lengthLimit)
    {
        uint[] counts = new uint[16];
        foreach (byte localLen in lengths)
        {
            counts[Math.Min(localLen, lengthLimit)]++;
        }

        uint total = 0;
        for (int i = 1; i <= lengthLimit; i++)
        {
            total += counts[i] << (lengthLimit - i);
        }

        while (total > (1u << lengthLimit))
        {
            int i = lengthLimit - 1;
            while (counts[i] == 0)
                i--;
            counts[i]--;
            counts[lengthLimit]--;
            counts[i + 1] += 2;
            total--;
        }

        // Reassign lengths
        var indexed = new List<(int index, uint freq)>();
        for (int i = 0; i < frequencies.Length; i++)
        {
            indexed.Add((i, frequencies[i]));
        }
        indexed.Sort((a, b) => a.freq.CompareTo(b.freq));

        byte len = lengthLimit;
        foreach (var (idx, freq) in indexed)
        {
            if (freq > 0)
            {
                while (counts[len] == 0)
                    len--;
                lengths[idx] = len;
                counts[len]--;
            }
        }
    }

    private static ushort ReverseBits(ushort value, int bitCount)
    {
        ushort result = 0;
        for (int i = 0; i < bitCount; i++)
        {
            result = (ushort)((result << 1) | (value & 1));
            value >>= 1;
        }
        return result;
    }
}
