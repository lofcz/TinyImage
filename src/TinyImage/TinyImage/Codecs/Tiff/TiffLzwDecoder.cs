using System;
using System.Collections.Generic;

namespace TinyImage.Codecs.Tiff;

/// <summary>
/// LZW decompression for TIFF images.
/// Based on tiff TypeScript lzw.ts.
/// Note: TIFF uses MSB-first bit packing (unlike GIF which uses LSB-first).
/// </summary>
internal static class TiffLzwDecoder
{
    private const int ClearCode = 256;
    private const int EoiCode = 257;
    private const int TableStart = 258;
    private const int MinBitLength = 9;
    private const int MaxTableSize = 4096;

    // Precomputed masks for bit extraction
    private static readonly int[] AndTable = { 511, 1023, 2047, 4095 };
    private static readonly int[] BitJumps = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 511, 1023, 2047, 4095 };

    /// <summary>
    /// Decodes LZW compressed TIFF data.
    /// </summary>
    /// <param name="compressedData">The compressed data.</param>
    /// <param name="expectedSize">The expected uncompressed size.</param>
    /// <returns>The decompressed data.</returns>
    public static byte[] Decode(byte[] compressedData, int expectedSize)
    {
        if (compressedData == null || compressedData.Length == 0)
            return Array.Empty<byte>();

        var decoder = new Decoder(compressedData, expectedSize);
        return decoder.Decode();
    }

    private sealed class Decoder
    {
        private readonly byte[] _input;
        private readonly int _expectedSize;
        private readonly List<byte> _output;

        // String table - using jagged array for performance
        private readonly byte[][] _stringTable;

        // Bit reading state
        private int _nextData;
        private int _nextBits;
        private int _bytePointer;

        // Table state
        private int _tableLength;
        private int _currentBitLength;

        public Decoder(byte[] input, int expectedSize)
        {
            _input = input;
            _expectedSize = expectedSize;
            _output = new List<byte>(expectedSize);

            // Initialize string table
            _stringTable = new byte[MaxTableSize][];
            for (int i = 0; i < 256; i++)
            {
                _stringTable[i] = new byte[] { (byte)i };
            }

            InitializeTable();
        }

        public byte[] Decode()
        {
            int code;
            int oldCode = 0;

            while ((code = GetNextCode()) != EoiCode)
            {
                if (code == ClearCode)
                {
                    InitializeTable();
                    code = GetNextCode();

                    if (code == EoiCode)
                        break;

                    WriteString(StringFromCode(code));
                    oldCode = code;
                }
                else if (IsInTable(code))
                {
                    var codeString = StringFromCode(code);
                    WriteString(codeString);
                    AddStringToTable(ConcatFirstByte(StringFromCode(oldCode), codeString));
                    oldCode = code;
                }
                else
                {
                    // Code not in table - special case: code == tableLength
                    var oldString = StringFromCode(oldCode);
                    var outString = ConcatFirstByte(oldString, oldString);
                    WriteString(outString);
                    AddStringToTable(outString);
                    oldCode = code;
                }

                // Check if we've reached expected size
                if (_output.Count >= _expectedSize)
                    break;
            }

            return _output.ToArray();
        }

        private void InitializeTable()
        {
            _tableLength = TableStart;
            _currentBitLength = MinBitLength;
        }

        private void WriteString(byte[] str)
        {
            int remaining = _expectedSize - _output.Count;
            int toWrite = Math.Min(str.Length, remaining);

            for (int i = 0; i < toWrite; i++)
            {
                _output.Add(str[i]);
            }
        }

        private byte[] StringFromCode(int code)
        {
            if (code < 0 || code >= _tableLength || _stringTable[code] == null)
            {
                // Invalid code - return empty to avoid crash
                return Array.Empty<byte>();
            }
            return _stringTable[code];
        }

        private bool IsInTable(int code)
        {
            return code < _tableLength;
        }

        private void AddStringToTable(byte[] str)
        {
            if (_tableLength < MaxTableSize)
            {
                _stringTable[_tableLength++] = str;

                // Increase bit length when reaching threshold
                if (_tableLength == BitJumps[_currentBitLength])
                {
                    _currentBitLength++;
                }
            }
        }

        private byte[] ConcatFirstByte(byte[] str1, byte[] str2)
        {
            if (str1 == null || str1.Length == 0)
            {
                return str2 != null && str2.Length > 0 ? new byte[] { str2[0] } : Array.Empty<byte>();
            }

            if (str2 == null || str2.Length == 0)
            {
                return str1;
            }

            var result = new byte[str1.Length + 1];
            Buffer.BlockCopy(str1, 0, result, 0, str1.Length);
            result[str1.Length] = str2[0];
            return result;
        }

        /// <summary>
        /// Reads the next LZW code from the bit stream.
        /// TIFF uses MSB-first bit packing.
        /// </summary>
        private int GetNextCode()
        {
            // Read bytes until we have enough bits
            while (_nextBits < _currentBitLength)
            {
                if (_bytePointer >= _input.Length)
                {
                    // End of data - return EOI
                    return EoiCode;
                }

                _nextData = (_nextData << 8) | (_input[_bytePointer++] & 0xFF);
                _nextBits += 8;
            }

            // Extract code (MSB-first)
            int code = (_nextData >> (_nextBits - _currentBitLength)) & AndTable[_currentBitLength - 9];
            _nextBits -= _currentBitLength;

            // Safety check for corrupt data
            if (_bytePointer > _input.Length + 10)
            {
                return EoiCode;
            }

            return code;
        }
    }
}
