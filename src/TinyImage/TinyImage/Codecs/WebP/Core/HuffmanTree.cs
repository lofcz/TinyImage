using System;
using System.Collections.Generic;

namespace TinyImage.Codecs.WebP.Core;

/// <summary>
/// Huffman tree for VP8L lossless decoding.
/// Translated from webp-rust huffman.rs
/// </summary>
internal class HuffmanTree
{
    private const int MaxAllowedCodeLength = 15;
    private const int MaxTableBits = 10;

    private readonly bool _isSingleNode;
    private readonly ushort _singleSymbol;
    private readonly ushort _tableMask;
    private readonly ushort[] _primaryTable;
    private readonly ushort[] _secondaryTable;

    private HuffmanTree(ushort singleSymbol)
    {
        _isSingleNode = true;
        _singleSymbol = singleSymbol;
        _primaryTable = null;
        _secondaryTable = null;
        _tableMask = 0;
    }

    private HuffmanTree(ushort[] primaryTable, ushort[] secondaryTable, ushort tableMask)
    {
        _isSingleNode = false;
        _singleSymbol = 0;
        _primaryTable = primaryTable;
        _secondaryTable = secondaryTable;
        _tableMask = tableMask;
    }

    /// <summary>
    /// Creates a tree with a single node (single symbol).
    /// </summary>
    public static HuffmanTree BuildSingleNode(ushort symbol)
    {
        return new HuffmanTree(symbol);
    }

    /// <summary>
    /// Creates a tree with two symbols.
    /// </summary>
    public static HuffmanTree BuildTwoNode(ushort zeroSymbol, ushort oneSymbol)
    {
        var primary = new ushort[] { (ushort)((1 << 12) | zeroSymbol), (ushort)((1 << 12) | oneSymbol) };
        return new HuffmanTree(primary, Array.Empty<ushort>(), 0x1);
    }

    /// <summary>
    /// Builds a Huffman tree from code lengths.
    /// </summary>
    public static HuffmanTree BuildImplicit(ushort[] codeLengths)
    {
        // Count symbols and build histogram
        int numSymbols = 0;
        int[] histogram = new int[MaxAllowedCodeLength + 1];
        for (int i = 0; i < codeLengths.Length; i++)
        {
            int length = codeLengths[i];
            histogram[length]++;
            if (length != 0)
                numSymbols++;
        }

        // Handle special cases
        if (numSymbols == 0)
            throw new WebPDecodingException("Huffman error: no symbols");

        if (numSymbols == 1)
        {
            for (int i = 0; i < codeLengths.Length; i++)
            {
                if (codeLengths[i] != 0)
                    return BuildSingleNode((ushort)i);
            }
        }

        // Determine max code length
        int maxLength = MaxAllowedCodeLength;
        while (maxLength > 1 && histogram[maxLength] == 0)
            maxLength--;

        // Sort symbols by code length
        int[] offsets = new int[16];
        int codespaceUsed = 0;
        offsets[1] = histogram[0];
        for (int i = 1; i < maxLength; i++)
        {
            offsets[i + 1] = offsets[i] + histogram[i];
            codespaceUsed = (codespaceUsed << 1) + histogram[i];
        }
        codespaceUsed = (codespaceUsed << 1) + histogram[maxLength];

        // Validate huffman tree
        if (codespaceUsed != (1 << maxLength))
            throw new WebPDecodingException("Huffman error: invalid tree");

        // Calculate table parameters
        int tableBits = Math.Min(maxLength, MaxTableBits);
        int tableSize = 1 << tableBits;
        ushort tableMask = (ushort)(tableSize - 1);
        ushort[] primaryTable = new ushort[tableSize];

        // Sort symbols by code length
        int[] nextIndex = (int[])offsets.Clone();
        ushort[] sortedSymbols = new ushort[codeLengths.Length];
        for (int symbol = 0; symbol < codeLengths.Length; symbol++)
        {
            int length = codeLengths[symbol];
            sortedSymbols[nextIndex[length]++] = (ushort)symbol;
        }

        int codeword = 0;
        int symbolIndex = histogram[0];

        // Populate primary table
        int primaryTableBits = (int)Math.Log(primaryTable.Length, 2);
        for (int length = 1; length <= primaryTableBits; length++)
        {
            int currentTableEnd = 1 << length;

            for (int j = 0; j < histogram[length]; j++)
            {
                ushort symbol = sortedSymbols[symbolIndex++];
                ushort entry = (ushort)((length << 12) | symbol);
                primaryTable[codeword] = entry;
                codeword = NextCodeword(codeword, (ushort)currentTableEnd);
            }

            // Double table size
            if (length < primaryTableBits)
            {
                Array.Copy(primaryTable, 0, primaryTable, currentTableEnd, currentTableEnd);
            }
        }

        // Populate secondary table
        var secondaryTableList = new List<ushort>();
        if (maxLength > primaryTableBits)
        {
            int subtableStart = 0;
            int subtablePrefix = -1;

            for (int length = primaryTableBits + 1; length <= maxLength; length++)
            {
                int subtableSize = 1 << (length - primaryTableBits);
                for (int j = 0; j < histogram[length]; j++)
                {
                    int prefix = codeword & ((1 << primaryTableBits) - 1);
                    if (prefix != subtablePrefix)
                    {
                        subtablePrefix = prefix;
                        subtableStart = secondaryTableList.Count;
                        primaryTable[prefix] = (ushort)((length << 12) | subtableStart);

                        while (secondaryTableList.Count < subtableStart + subtableSize)
                            secondaryTableList.Add(0);
                    }

                    ushort symbol = sortedSymbols[symbolIndex++];
                    int secondaryIndex = subtableStart + (codeword >> primaryTableBits);
                    if (secondaryIndex < secondaryTableList.Count)
                    {
                        secondaryTableList[secondaryIndex] = (ushort)((symbol << 4) | length);
                    }
                    codeword = NextCodeword(codeword, (ushort)(1 << length));
                }

                // Extend subtable if needed
                if (length < maxLength && (codeword & ((1 << primaryTableBits) - 1)) == subtablePrefix)
                {
                    int oldCount = secondaryTableList.Count;
                    for (int k = subtableStart; k < oldCount; k++)
                        secondaryTableList.Add(secondaryTableList[k]);
                    primaryTable[subtablePrefix] = (ushort)(((length + 1) << 12) | subtableStart);
                }
            }
        }

        return new HuffmanTree(primaryTable, secondaryTableList.ToArray(), tableMask);
    }

    private static int NextCodeword(int codeword, ushort tableSize)
    {
        if (codeword == tableSize - 1)
            return codeword;

        int leading = 31 - LeadingZeros((uint)(codeword ^ (tableSize - 1)));
        int bit = 1 << leading;
        codeword &= bit - 1;
        codeword |= bit;
        return codeword;
    }

    private static int LeadingZeros(uint value)
    {
        if (value == 0) return 32;
        int n = 0;
        if (value <= 0x0000FFFF) { n += 16; value <<= 16; }
        if (value <= 0x00FFFFFF) { n += 8; value <<= 8; }
        if (value <= 0x0FFFFFFF) { n += 4; value <<= 4; }
        if (value <= 0x3FFFFFFF) { n += 2; value <<= 2; }
        if (value <= 0x7FFFFFFF) { n += 1; }
        return n;
    }

    /// <summary>
    /// Returns true if this tree contains only a single symbol.
    /// </summary>
    public bool IsSingleNode => _isSingleNode;

    /// <summary>
    /// Reads a symbol from the bitstream using this Huffman tree.
    /// BitReader.Fill() should be called before this function.
    /// </summary>
    public ushort ReadSymbol(BitReader reader)
    {
        if (_isSingleNode)
            return _singleSymbol;

        ulong v = reader.PeekFull();
        ushort entry = _primaryTable[(int)(v & _tableMask)];
        int length = entry >> 12;

        if (length <= MaxTableBits)
        {
            reader.Consume(length);
            return (ushort)(entry & 0xFFF);
        }

        // Use secondary table
        int mask = (1 << (length - MaxTableBits)) - 1;
        int secondaryIndex = (entry & 0xFFF) + (int)((v >> MaxTableBits) & (uint)mask);
        ushort secondaryEntry = _secondaryTable[secondaryIndex];
        reader.Consume(secondaryEntry & 0xF);
        return (ushort)(secondaryEntry >> 4);
    }

    /// <summary>
    /// Peeks at the next symbol if it can be read with primary table only.
    /// Returns (bits, symbol) or null if secondary table needed.
    /// </summary>
    public (int bits, ushort symbol)? PeekSymbol(BitReader reader)
    {
        if (_isSingleNode)
            return (0, _singleSymbol);

        ulong v = reader.PeekFull();
        ushort entry = _primaryTable[(int)(v & _tableMask)];
        int length = entry >> 12;

        if (length <= MaxTableBits)
            return (length, (ushort)(entry & 0xFFF));

        return null;
    }
}

/// <summary>
/// Group of 5 Huffman trees used for VP8L decoding.
/// </summary>
internal class HuffmanCodeGroup
{
    public const int NumCodes = 5;

    public const int Green = 0;
    public const int Red = 1;
    public const int Blue = 2;
    public const int Alpha = 3;
    public const int Distance = 4;

    public HuffmanTree[] Trees { get; } = new HuffmanTree[NumCodes];

    public HuffmanTree this[int index]
    {
        get => Trees[index];
        set => Trees[index] = value;
    }
}
