using System;

namespace TinyImage.Codecs.WebP.Core;

/// <summary>
/// Boolean arithmetic decoder for VP8 lossy decoding.
/// Translated from webp-rust vp8_arithmetic_decoder.rs
/// </summary>
internal class ArithmeticDecoder
{
    private byte[][] _chunks;
    private int _chunkIndex;
    private ulong _value;
    private uint _range;
    private int _bitCount;
    private byte[] _finalBytes;
    private int _finalBytesRemaining;
    private const int FinalBytesRemainingEof = -14;

    public ArithmeticDecoder()
    {
        _chunks = Array.Empty<byte[]>();
        _chunkIndex = 0;
        _value = 0;
        _range = 255;
        _bitCount = -8;
        _finalBytes = new byte[3];
        _finalBytesRemaining = FinalBytesRemainingEof;
    }

    /// <summary>
    /// Initializes the decoder with data.
    /// </summary>
    public void Init(byte[] data, int length)
    {
        if (data == null || length == 0)
            throw new WebPDecodingException("Not enough init data");

        // Split data into 4-byte chunks
        int numChunks = (length + 3) / 4;
        _chunks = new byte[numChunks][];

        for (int i = 0; i < numChunks; i++)
        {
            _chunks[i] = new byte[4];
            int start = i * 4;
            int bytesToCopy = Math.Min(4, length - start);
            Array.Copy(data, start, _chunks[i], 0, bytesToCopy);
        }

        // Handle final bytes
        int lastChunkBytes = length % 4;
        if (lastChunkBytes == 0 && length > 0)
            lastChunkBytes = 4;

        _finalBytesRemaining = 0;
        if (lastChunkBytes < 4 && numChunks > 0)
        {
            byte[] lastChunk = _chunks[numChunks - 1];
            _chunks = ResizeArray(_chunks, numChunks - 1);

            _finalBytes = new byte[3];
            for (int i = 0; i < lastChunkBytes && i < 3; i++)
                _finalBytes[i] = lastChunk[i];
            _finalBytesRemaining = lastChunkBytes;
        }

        _chunkIndex = 0;
        _value = 0;
        _range = 255;
        _bitCount = -8;
    }

    private static byte[][] ResizeArray(byte[][] array, int newSize)
    {
        byte[][] result = new byte[newSize][];
        Array.Copy(array, result, Math.Min(array.Length, newSize));
        return result;
    }

    /// <summary>
    /// Returns true if we've read past the end of file.
    /// </summary>
    public bool IsPastEof => _finalBytesRemaining == FinalBytesRemainingEof;

    /// <summary>
    /// Reads a boolean with the given probability (0-255).
    /// Probability represents P(false), so 128 = 50/50.
    /// </summary>
    public bool ReadBool(byte probability)
    {
        if (_bitCount < 0)
        {
            if (_chunkIndex < _chunks.Length)
            {
                byte[] chunk = _chunks[_chunkIndex++];
                uint v = (uint)(chunk[0] << 24 | chunk[1] << 16 | chunk[2] << 8 | chunk[3]);
                _value <<= 32;
                _value |= v;
                _bitCount += 32;
            }
            else
            {
                LoadFromFinalBytes();
                if (IsPastEof)
                    throw new WebPDecodingException("Bitstream error");
            }
        }

        uint split = 1 + (((_range - 1) * probability) >> 8);
        ulong bigSplit = (ulong)split << _bitCount;

        bool result;
        if (_value >= bigSplit)
        {
            _range -= split;
            _value -= bigSplit;
            result = true;
        }
        else
        {
            _range = split;
            result = false;
        }

        // Normalize
        int shift = LeadingZeros(_range) - 24;
        if (shift < 0) shift = 0;
        _range <<= shift;
        _bitCount -= shift;

        return result;
    }

    /// <summary>
    /// Reads a flag (boolean with probability 128).
    /// </summary>
    public bool ReadFlag()
    {
        return ReadBool(128);
    }

    /// <summary>
    /// Reads a sign bit (boolean with probability 128, after at least one other bit).
    /// </summary>
    public bool ReadSign()
    {
        return ReadBool(128);
    }

    /// <summary>
    /// Reads n bits as a literal value.
    /// </summary>
    public byte ReadLiteral(int n)
    {
        byte v = 0;
        for (int i = 0; i < n; i++)
        {
            bool b = ReadFlag();
            v = (byte)((v << 1) + (b ? 1 : 0));
        }
        return v;
    }

    /// <summary>
    /// Reads an optional signed value: flag + magnitude + sign.
    /// </summary>
    public int ReadOptionalSignedValue(int n)
    {
        if (!ReadFlag())
            return 0;

        int magnitude = ReadLiteral(n);
        bool negative = ReadFlag();
        return negative ? -magnitude : magnitude;
    }

    /// <summary>
    /// Reads a value using a probability tree.
    /// </summary>
    public int ReadWithTree(sbyte[] tree, byte[] probs)
    {
        int index = 0;
        while (true)
        {
            bool b = ReadBool(probs[index / 2]);
            int nextIndex = b ? tree[index + 1] : tree[index];

            if (nextIndex <= 0)
                return -nextIndex;

            index = nextIndex;
        }
    }

    /// <summary>
    /// Reads a value using a tree node array (optimized format).
    /// </summary>
    public int ReadWithTreeNodes(TreeNode[] nodes, int startIndex = 0)
    {
        int index = startIndex;
        while (true)
        {
            TreeNode node = nodes[index];
            bool b = ReadBool(node.Prob);
            byte next = b ? node.Right : node.Left;

            if ((next & 0x80) != 0)
                return next & 0x7F;

            index = next;
        }
    }

    private void LoadFromFinalBytes()
    {
        if (_finalBytesRemaining > 0)
        {
            _finalBytesRemaining--;
            byte b = _finalBytes[0];
            // Rotate final bytes
            _finalBytes[0] = _finalBytes[1];
            _finalBytes[1] = _finalBytes[2];
            _finalBytes[2] = 0;

            _value <<= 8;
            _value |= b;
            _bitCount += 8;
        }
        else if (_finalBytesRemaining == 0)
        {
            // Allow reading one byte past end (libwebp compatibility)
            _finalBytesRemaining--;
            _value <<= 8;
            _bitCount += 8;
        }
        else
        {
            _finalBytesRemaining = FinalBytesRemainingEof;
        }
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
}

/// <summary>
/// Tree node for optimized probability tree traversal.
/// </summary>
internal struct TreeNode
{
    public byte Left;
    public byte Right;
    public byte Prob;
    public byte Index;

    public TreeNode(byte left, byte right, byte prob, byte index)
    {
        Left = left;
        Right = right;
        Prob = prob;
        Index = index;
    }

    /// <summary>
    /// Prepares a branch value from a tree index.
    /// </summary>
    public static byte PrepareBranch(sbyte t)
    {
        if (t > 0)
            return (byte)(t / 2);
        return (byte)(0x80 | (-t));
    }

    /// <summary>
    /// Extracts the value from a branch.
    /// </summary>
    public static int ValueFromBranch(byte t)
    {
        return t & 0x7F;
    }
}
