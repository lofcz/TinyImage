using System;
using System.IO;

namespace TinyImage.Codecs.Bmp;

/// <summary>
/// Huffman decoder for OS/2 1-bit BMP images using ITU-T T.4 (G3 fax) compression.
/// Based on the bmplib reference implementation.
/// </summary>
internal sealed class BmpHuffmanDecoder
{
    private readonly Stream _stream;
    private uint _buffer;
    private int _bufferBits;

    // Huffman tree nodes
    private static readonly HuffmanNode[] Nodes;
    private static readonly int WhiteRoot;
    private static readonly int BlackRoot;

    static BmpHuffmanDecoder()
    {
        // Build Huffman trees from ITU-T T.4 codes
        Nodes = new HuffmanNode[512];
        var nodeCount = 0;

        // Build white codes tree
        WhiteRoot = BuildTree(WhiteTermCodes, WhiteMakeupCodes, ref nodeCount);

        // Build black codes tree
        BlackRoot = BuildTree(BlackTermCodes, BlackMakeupCodes, ref nodeCount);
    }

    public BmpHuffmanDecoder(Stream stream)
    {
        _stream = stream;
        _buffer = 0;
        _bufferBits = 0;
    }

    /// <summary>
    /// Decodes a single line of 1-bit Huffman compressed data.
    /// </summary>
    /// <param name="line">Output buffer for the decoded line (1 byte per pixel, 0 or 255).</param>
    /// <param name="width">Width of the image in pixels.</param>
    /// <param name="blackIsZero">If true, black pixels are palette index 0; if false, index 1.</param>
    /// <returns>True if successful, false if error or truncated.</returns>
    public bool DecodeLine(Span<byte> line, int width, bool blackIsZero = false)
    {
        int x = 0;
        bool isBlack = false;

        while (x < width)
        {
            FillBuffer();

            if (_bufferBits == 0)
                return false; // Truncated

            // Check for EOL (11 zeros followed by 1)
            if ((_buffer & 0xFF000000) == 0)
            {
                if (!SkipEol())
                    return false;

                if (x == 0) // Ignore EOL at start of line
                    continue;

                break; // End of line
            }

            int runLength = DecodeRun(isBlack);
            if (runLength < 0)
            {
                // Invalid code - try to find next EOL
                FindEol();
                break;
            }

            // Clamp run length to remaining width
            if (runLength > width - x)
                runLength = width - x;

            // Fill the run
            byte pixelValue = (byte)((isBlack ^ blackIsZero) ? 0 : 255);
            for (int i = 0; i < runLength && x < width; i++, x++)
            {
                line[x] = pixelValue;
            }

            isBlack = !isBlack;
        }

        return true;
    }

    private int DecodeRun(bool isBlack)
    {
        int total = 0;
        int runLength;

        do
        {
            FillBuffer();
            runLength = FindCode(isBlack, out bool isMakeup);

            if (runLength < 0)
                return -1;

            total += runLength;

            if (!isMakeup)
                break;

        } while (total < int.MaxValue - 2560);

        return total;
    }

    private int FindCode(bool isBlack, out bool isMakeup)
    {
        isMakeup = false;
        int nodeIndex = isBlack ? BlackRoot : WhiteRoot;
        int bitsUsed = 0;

        while (nodeIndex >= 0 && !Nodes[nodeIndex].IsTerminal && bitsUsed < _bufferBits)
        {
            bool bit = (_buffer & 0x80000000) != 0;
            nodeIndex = bit ? Nodes[nodeIndex].Right : Nodes[nodeIndex].Left;
            _buffer <<= 1;
            _bufferBits--;
            bitsUsed++;
        }

        if (nodeIndex < 0 || !Nodes[nodeIndex].IsTerminal)
            return -1;

        isMakeup = Nodes[nodeIndex].IsMakeup;
        return Nodes[nodeIndex].Value;
    }

    private void FillBuffer()
    {
        while (_bufferBits <= 24)
        {
            int b = _stream.ReadByte();
            if (b < 0)
                break;

            _buffer |= (uint)b << (24 - _bufferBits);
            _bufferBits += 8;
        }
    }

    private bool SkipEol()
    {
        FillBuffer();
        while (_bufferBits > 0)
        {
            if (_buffer == 0)
            {
                _bufferBits = 0;
                FillBuffer();
                continue;
            }

            while ((_buffer & 0x80000000) == 0)
            {
                _buffer <<= 1;
                _bufferBits--;
            }

            _buffer <<= 1;
            _bufferBits--;
            return true;
        }
        return false;
    }

    private bool FindEol()
    {
        // Look for 11 consecutive zeros followed by a 1
        FillBuffer();
        while (_bufferBits > 11)
        {
            if ((_buffer & 0xFFE00000) == 0) // 11 zeros at top
            {
                _buffer <<= 11;
                _bufferBits -= 11;
                return SkipEol();
            }

            _buffer <<= 1;
            _bufferBits--;

            if (_bufferBits < 12)
                FillBuffer();
        }
        return false;
    }

    #region Huffman Tree Building

    private static int BuildTree(HuffmanCode[] termCodes, HuffmanCode[] makeupCodes, ref int nodeCount)
    {
        int root = nodeCount++;
        Nodes[root] = new HuffmanNode();

        // Add terminal codes
        foreach (var code in termCodes)
        {
            AddCode(root, code.Bits, code.Length, code.Value, false, ref nodeCount);
        }

        // Add makeup codes
        foreach (var code in makeupCodes)
        {
            AddCode(root, code.Bits, code.Length, code.Value, true, ref nodeCount);
        }

        return root;
    }

    private static void AddCode(int root, int bits, int length, int value, bool isMakeup, ref int nodeCount)
    {
        int node = root;

        for (int i = length - 1; i >= 0; i--)
        {
            bool bit = ((bits >> i) & 1) != 0;

            if (bit)
            {
                if (Nodes[node].Right < 0)
                {
                    Nodes[node].Right = nodeCount++;
                    Nodes[Nodes[node].Right] = new HuffmanNode();
                }
                node = Nodes[node].Right;
            }
            else
            {
                if (Nodes[node].Left < 0)
                {
                    Nodes[node].Left = nodeCount++;
                    Nodes[Nodes[node].Left] = new HuffmanNode();
                }
                node = Nodes[node].Left;
            }
        }

        Nodes[node].IsTerminal = true;
        Nodes[node].Value = value;
        Nodes[node].IsMakeup = isMakeup;
    }

    private struct HuffmanNode
    {
        public int Left;
        public int Right;
        public int Value;
        public bool IsTerminal;
        public bool IsMakeup;

        public HuffmanNode()
        {
            Left = -1;
            Right = -1;
            Value = 0;
            IsTerminal = false;
            IsMakeup = false;
        }
    }

    private readonly struct HuffmanCode
    {
        public readonly int Value;
        public readonly int Bits;
        public readonly int Length;

        public HuffmanCode(int value, int bits, int length)
        {
            Value = value;
            Bits = bits;
            Length = length;
        }
    }

    #endregion

    #region ITU-T T.4 Huffman Code Tables

    // White terminal codes (run lengths 0-63)
    private static readonly HuffmanCode[] WhiteTermCodes =
    [
        new(0, 0b00110101, 8),
        new(1, 0b000111, 6),
        new(2, 0b0111, 4),
        new(3, 0b1000, 4),
        new(4, 0b1011, 4),
        new(5, 0b1100, 4),
        new(6, 0b1110, 4),
        new(7, 0b1111, 4),
        new(8, 0b10011, 5),
        new(9, 0b10100, 5),
        new(10, 0b00111, 5),
        new(11, 0b01000, 5),
        new(12, 0b001000, 6),
        new(13, 0b000011, 6),
        new(14, 0b110100, 6),
        new(15, 0b110101, 6),
        new(16, 0b101010, 6),
        new(17, 0b101011, 6),
        new(18, 0b0100111, 7),
        new(19, 0b0001100, 7),
        new(20, 0b0001000, 7),
        new(21, 0b0010111, 7),
        new(22, 0b0000011, 7),
        new(23, 0b0000100, 7),
        new(24, 0b0101000, 7),
        new(25, 0b0101011, 7),
        new(26, 0b0010011, 7),
        new(27, 0b0100100, 7),
        new(28, 0b0011000, 7),
        new(29, 0b00000010, 8),
        new(30, 0b00000011, 8),
        new(31, 0b00011010, 8),
        new(32, 0b00011011, 8),
        new(33, 0b00010010, 8),
        new(34, 0b00010011, 8),
        new(35, 0b00010100, 8),
        new(36, 0b00010101, 8),
        new(37, 0b00010110, 8),
        new(38, 0b00010111, 8),
        new(39, 0b00101000, 8),
        new(40, 0b00101001, 8),
        new(41, 0b00101010, 8),
        new(42, 0b00101011, 8),
        new(43, 0b00101100, 8),
        new(44, 0b00101101, 8),
        new(45, 0b00000100, 8),
        new(46, 0b00000101, 8),
        new(47, 0b00001010, 8),
        new(48, 0b00001011, 8),
        new(49, 0b01010010, 8),
        new(50, 0b01010011, 8),
        new(51, 0b01010100, 8),
        new(52, 0b01010101, 8),
        new(53, 0b00100100, 8),
        new(54, 0b00100101, 8),
        new(55, 0b01011000, 8),
        new(56, 0b01011001, 8),
        new(57, 0b01011010, 8),
        new(58, 0b01011011, 8),
        new(59, 0b01001010, 8),
        new(60, 0b01001011, 8),
        new(61, 0b00110010, 8),
        new(62, 0b00110011, 8),
        new(63, 0b00110100, 8),
    ];

    // Black terminal codes (run lengths 0-63)
    private static readonly HuffmanCode[] BlackTermCodes =
    [
        new(0, 0b0000110111, 10),
        new(1, 0b010, 3),
        new(2, 0b11, 2),
        new(3, 0b10, 2),
        new(4, 0b011, 3),
        new(5, 0b0011, 4),
        new(6, 0b0010, 4),
        new(7, 0b00011, 5),
        new(8, 0b000101, 6),
        new(9, 0b000100, 6),
        new(10, 0b0000100, 7),
        new(11, 0b0000101, 7),
        new(12, 0b0000111, 7),
        new(13, 0b00000100, 8),
        new(14, 0b00000111, 8),
        new(15, 0b000011000, 9),
        new(16, 0b0000010111, 10),
        new(17, 0b0000011000, 10),
        new(18, 0b0000001000, 10),
        new(19, 0b00001100111, 11),
        new(20, 0b00001101000, 11),
        new(21, 0b00001101100, 11),
        new(22, 0b00000110111, 11),
        new(23, 0b00000101000, 11),
        new(24, 0b00000010111, 11),
        new(25, 0b00000011000, 11),
        new(26, 0b000011001010, 12),
        new(27, 0b000011001011, 12),
        new(28, 0b000011001100, 12),
        new(29, 0b000011001101, 12),
        new(30, 0b000001101000, 12),
        new(31, 0b000001101001, 12),
        new(32, 0b000001101010, 12),
        new(33, 0b000001101011, 12),
        new(34, 0b000011010010, 12),
        new(35, 0b000011010011, 12),
        new(36, 0b000011010100, 12),
        new(37, 0b000011010101, 12),
        new(38, 0b000011010110, 12),
        new(39, 0b000011010111, 12),
        new(40, 0b000001101100, 12),
        new(41, 0b000001101101, 12),
        new(42, 0b000011011010, 12),
        new(43, 0b000011011011, 12),
        new(44, 0b000001010100, 12),
        new(45, 0b000001010101, 12),
        new(46, 0b000001010110, 12),
        new(47, 0b000001010111, 12),
        new(48, 0b000001100100, 12),
        new(49, 0b000001100101, 12),
        new(50, 0b000001010010, 12),
        new(51, 0b000001010011, 12),
        new(52, 0b000000100100, 12),
        new(53, 0b000000110111, 12),
        new(54, 0b000000111000, 12),
        new(55, 0b000000100111, 12),
        new(56, 0b000000101000, 12),
        new(57, 0b000001011000, 12),
        new(58, 0b000001011001, 12),
        new(59, 0b000000101011, 12),
        new(60, 0b000000101100, 12),
        new(61, 0b000001011010, 12),
        new(62, 0b000001100110, 12),
        new(63, 0b000001100111, 12),
    ];

    // White makeup codes (run lengths 64, 128, 192, ... up to 2560)
    private static readonly HuffmanCode[] WhiteMakeupCodes =
    [
        new(64, 0b11011, 5),
        new(128, 0b10010, 5),
        new(192, 0b010111, 6),
        new(256, 0b0110111, 7),
        new(320, 0b00110110, 8),
        new(384, 0b00110111, 8),
        new(448, 0b01100100, 8),
        new(512, 0b01100101, 8),
        new(576, 0b01101000, 8),
        new(640, 0b01100111, 8),
        new(704, 0b011001100, 9),
        new(768, 0b011001101, 9),
        new(832, 0b011010010, 9),
        new(896, 0b011010011, 9),
        new(960, 0b011010100, 9),
        new(1024, 0b011010101, 9),
        new(1088, 0b011010110, 9),
        new(1152, 0b011010111, 9),
        new(1216, 0b011011000, 9),
        new(1280, 0b011011001, 9),
        new(1344, 0b011011010, 9),
        new(1408, 0b011011011, 9),
        new(1472, 0b010011000, 9),
        new(1536, 0b010011001, 9),
        new(1600, 0b010011010, 9),
        new(1664, 0b011000, 6),
        new(1728, 0b010011011, 9),
        new(1792, 0b00000001000, 11),
        new(1856, 0b00000001100, 11),
        new(1920, 0b00000001101, 11),
        new(1984, 0b000000010010, 12),
        new(2048, 0b000000010011, 12),
        new(2112, 0b000000010100, 12),
        new(2176, 0b000000010101, 12),
        new(2240, 0b000000010110, 12),
        new(2304, 0b000000010111, 12),
        new(2368, 0b000000011100, 12),
        new(2432, 0b000000011101, 12),
        new(2496, 0b000000011110, 12),
        new(2560, 0b000000011111, 12),
    ];

    // Black makeup codes
    private static readonly HuffmanCode[] BlackMakeupCodes =
    [
        new(64, 0b0000001111, 10),
        new(128, 0b000011001000, 12),
        new(192, 0b000011001001, 12),
        new(256, 0b000001011011, 12),
        new(320, 0b000000110011, 12),
        new(384, 0b000000110100, 12),
        new(448, 0b000000110101, 12),
        new(512, 0b0000001101100, 13),
        new(576, 0b0000001101101, 13),
        new(640, 0b0000001001010, 13),
        new(704, 0b0000001001011, 13),
        new(768, 0b0000001001100, 13),
        new(832, 0b0000001001101, 13),
        new(896, 0b0000001110010, 13),
        new(960, 0b0000001110011, 13),
        new(1024, 0b0000001110100, 13),
        new(1088, 0b0000001110101, 13),
        new(1152, 0b0000001110110, 13),
        new(1216, 0b0000001110111, 13),
        new(1280, 0b0000001010010, 13),
        new(1344, 0b0000001010011, 13),
        new(1408, 0b0000001010100, 13),
        new(1472, 0b0000001010101, 13),
        new(1536, 0b0000001011010, 13),
        new(1600, 0b0000001011011, 13),
        new(1664, 0b0000001100100, 13),
        new(1728, 0b0000001100101, 13),
        new(1792, 0b00000001000, 11),
        new(1856, 0b00000001100, 11),
        new(1920, 0b00000001101, 11),
        new(1984, 0b000000010010, 12),
        new(2048, 0b000000010011, 12),
        new(2112, 0b000000010100, 12),
        new(2176, 0b000000010101, 12),
        new(2240, 0b000000010110, 12),
        new(2304, 0b000000010111, 12),
        new(2368, 0b000000011100, 12),
        new(2432, 0b000000011101, 12),
        new(2496, 0b000000011110, 12),
        new(2560, 0b000000011111, 12),
    ];

    #endregion
}
