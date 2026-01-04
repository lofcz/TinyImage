using System;
using System.Collections.Generic;

namespace TinyImage.Codecs.WebP.Core;

/// <summary>
/// Boolean arithmetic encoder for VP8 lossy encoding.
/// Translated from webp-rust vp8_arithmetic_encoder.rs
/// </summary>
internal class ArithmeticEncoder
{
    private readonly List<byte> _buffer;
    private uint _bottom;
    private uint _range;
    private int _bitNum;

    public ArithmeticEncoder()
    {
        _buffer = new List<byte>();
        _bottom = 0;
        _range = 255;
        _bitNum = 24;
    }

    /// <summary>
    /// Writes a boolean with the given probability.
    /// </summary>
    public void WriteBool(bool value, byte probability)
    {
        uint split = 1 + (((_range - 1) * probability) >> 8);

        if (value)
        {
            _bottom += split;
            _range -= split;
        }
        else
        {
            _range = split;
        }

        while (_range < 128)
        {
            _range <<= 1;

            if ((_bottom & (1u << 31)) != 0)
            {
                AddOneToOutput();
            }

            _bottom <<= 1;
            _bitNum--;

            if (_bitNum == 0)
            {
                byte newValue = (byte)(_bottom >> 24);
                _buffer.Add(newValue);
                _bottom &= (1u << 24) - 1;
                _bitNum = 8;
            }
        }
    }

    /// <summary>
    /// Writes a flag (boolean with probability 128).
    /// </summary>
    public void WriteFlag(bool flag)
    {
        WriteBool(flag, 128);
    }

    /// <summary>
    /// Writes n bits as a literal value.
    /// </summary>
    public void WriteLiteral(int numBits, byte value)
    {
        for (int bit = numBits - 1; bit >= 0; bit--)
        {
            bool b = ((1 << bit) & value) != 0;
            WriteBool(b, 128);
        }
    }

    /// <summary>
    /// Writes an optional signed value: flag + magnitude + sign.
    /// </summary>
    public void WriteOptionalSignedValue(int numBits, sbyte? value)
    {
        WriteFlag(value.HasValue);
        if (value.HasValue)
        {
            byte absValue = (byte)Math.Abs(value.Value);
            WriteLiteral(numBits, absValue);
            bool positive = value.Value >= 0;
            WriteFlag(positive);
        }
    }

    /// <summary>
    /// Writes a value using a probability tree.
    /// </summary>
    public void WriteWithTree(sbyte[] tree, byte[] probs, sbyte value)
    {
        WriteWithTreeStartIndex(tree, probs, value, 0);
    }

    /// <summary>
    /// Writes a value using a probability tree starting at a given node.
    /// The tree uses pairs: tree[2*n] is left branch, tree[2*n+1] is right branch.
    /// Negative values are leaves (token = -value), positive values are branch indices.
    /// </summary>
    public void WriteWithTreeStartIndex(sbyte[] tree, byte[] probs, sbyte value, int startIndex)
    {
        // startIndex is the raw tree index (0 or 2 typically)
        // Convert to node index for probability lookup
        int nodeIndex = startIndex / 2;
        
        while (true)
        {
            int treeIdx = nodeIndex * 2;
            sbyte leftVal = tree[treeIdx];
            sbyte rightVal = tree[treeIdx + 1];
            
            // Check if target value is in left subtree
            bool goLeft = IsValueInSubtree(tree, leftVal, value);
            
            // Write the decision bit: false = left, true = right
            WriteBool(!goLeft, probs[nodeIndex]);
            
            if (goLeft)
            {
                if (leftVal <= 0)
                    return; // Found the leaf
                nodeIndex = leftVal / 2; // Go to left subtree
            }
            else
            {
                if (rightVal <= 0)
                    return; // Found the leaf
                nodeIndex = rightVal / 2; // Go to right subtree
            }
        }
    }
    
    /// <summary>
    /// Checks if a value exists in the subtree rooted at the given branch.
    /// </summary>
    private static bool IsValueInSubtree(sbyte[] tree, sbyte branch, sbyte targetValue)
    {
        if (branch <= 0)
        {
            // Leaf node: check if this is the target value
            return branch == -targetValue;
        }
        
        // Internal node: recursively check both children
        int nodeIndex = branch / 2;
        int treeIdx = nodeIndex * 2;
        return IsValueInSubtree(tree, tree[treeIdx], targetValue) ||
               IsValueInSubtree(tree, tree[treeIdx + 1], targetValue);
    }

    /// <summary>
    /// Flushes remaining bits and returns the encoded buffer.
    /// </summary>
    public byte[] FlushAndGetBuffer()
    {
        int c = _bitNum;
        uint v = _bottom;

        if ((_bottom & (1u << (32 - _bitNum))) != 0)
        {
            AddOneToOutput();
        }

        v <<= (c & 0x7);
        c = (c >> 3) - 1;
        while (c >= 0)
        {
            v <<= 8;
            c--;
        }

        c = 3;
        while (c >= 0)
        {
            _buffer.Add((byte)(v >> 24));
            v <<= 8;
            c--;
        }

        return _buffer.ToArray();
    }

    private void AddOneToOutput()
    {
        // Go back and add one to existing values
        for (int i = _buffer.Count - 1; i >= 0; i--)
        {
            if (_buffer[i] < 255)
            {
                _buffer[i]++;
                break;
            }
            _buffer.RemoveAt(i);
        }
    }
}
