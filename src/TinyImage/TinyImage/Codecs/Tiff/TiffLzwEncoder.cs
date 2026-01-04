using System;
using System.Collections.Generic;
using System.IO;

namespace TinyImage.Codecs.Tiff;

/// <summary>
/// LZW compression for TIFF images.
/// TIFF uses MSB-first bit packing with 8-bit minimum code size.
/// </summary>
internal sealed class TiffLzwEncoder
{
    private const int ClearCode = 256;
    private const int EoiCode = 257;
    private const int TableStart = 258;
    private const int MinBitLength = 9;
    private const int MaxBitLength = 12;
    private const int MaxTableSize = 4096;

    private readonly Dictionary<string, int> _stringTable;
    private int _tableSize;
    private int _bitLength;

    // Bit output state
    private int _bitBuffer;
    private int _bitCount;
    private readonly List<byte> _output;

    public TiffLzwEncoder()
    {
        _stringTable = new Dictionary<string, int>();
        _output = new List<byte>();
        InitializeTable();
    }

    /// <summary>
    /// Compresses data using LZW algorithm.
    /// </summary>
    public static byte[] Encode(byte[] data)
    {
        if (data == null || data.Length == 0)
            return new byte[] { 0x80, 0x01 }; // Clear + EOI

        var encoder = new TiffLzwEncoder();
        return encoder.CompressInternal(data);
    }

    private void InitializeTable()
    {
        _stringTable.Clear();
        for (int i = 0; i < 256; i++)
        {
            _stringTable[((char)i).ToString()] = i;
        }
        _tableSize = TableStart;
        _bitLength = MinBitLength;
    }

    private byte[] CompressInternal(byte[] data)
    {
        _output.Clear();
        _bitBuffer = 0;
        _bitCount = 0;

        // Write clear code
        WriteCode(ClearCode);

        if (data.Length == 0)
        {
            WriteCode(EoiCode);
            FlushBits();
            return _output.ToArray();
        }

        string current = ((char)data[0]).ToString();

        for (int i = 1; i < data.Length; i++)
        {
            char c = (char)data[i];
            string combined = current + c;

            if (_stringTable.ContainsKey(combined))
            {
                current = combined;
            }
            else
            {
                // Output code for current
                WriteCode(_stringTable[current]);

                // Add combined to table if there's room
                if (_tableSize < MaxTableSize)
                {
                    _stringTable[combined] = _tableSize++;

                    // Check if we need to increase bit length
                    if (_tableSize > (1 << _bitLength) && _bitLength < MaxBitLength)
                    {
                        _bitLength++;
                    }
                }
                else
                {
                    // Table full - emit clear code and reset
                    WriteCode(ClearCode);
                    InitializeTable();
                }

                current = c.ToString();
            }
        }

        // Output final code
        WriteCode(_stringTable[current]);

        // Write EOI
        WriteCode(EoiCode);

        // Flush remaining bits
        FlushBits();

        return _output.ToArray();
    }

    private void WriteCode(int code)
    {
        // MSB-first bit packing
        _bitBuffer = (_bitBuffer << _bitLength) | code;
        _bitCount += _bitLength;

        while (_bitCount >= 8)
        {
            _bitCount -= 8;
            _output.Add((byte)((_bitBuffer >> _bitCount) & 0xFF));
        }
    }

    private void FlushBits()
    {
        if (_bitCount > 0)
        {
            _output.Add((byte)((_bitBuffer << (8 - _bitCount)) & 0xFF));
        }
    }
}
