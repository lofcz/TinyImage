using System;
using System.IO;

namespace TinyImage.Codecs.WebP.Core;

/// <summary>
/// Bit-level writer for VP8L lossless encoding.
/// Translated from webp-rust encoder.rs:BitWriter
/// </summary>
internal class BitWriter
{
    private readonly Stream _stream;
    private ulong _buffer;
    private int _nbits;

    public BitWriter(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _buffer = 0;
        _nbits = 0;
    }

    /// <summary>
    /// Writes n bits to the stream.
    /// </summary>
    public void WriteBits(ulong bits, int n)
    {
        if (n < 0 || n > 64)
            throw new ArgumentOutOfRangeException(nameof(n));

        _buffer |= bits << _nbits;
        _nbits += n;

        if (_nbits >= 64)
        {
            // Write 8 bytes
            byte[] bytes = BitConverter.GetBytes(_buffer);
            if (!BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            _stream.Write(bytes, 0, 8);

            _nbits -= 64;
            _buffer = (n > _nbits) ? (bits >> (n - _nbits)) : 0;
        }
    }

    /// <summary>
    /// Writes a single bit.
    /// </summary>
    public void WriteBit(bool bit)
    {
        WriteBits(bit ? 1UL : 0UL, 1);
    }

    /// <summary>
    /// Flushes remaining bits to the stream, padding to byte boundary.
    /// </summary>
    public void Flush()
    {
        // Pad to byte boundary
        if (_nbits % 8 != 0)
        {
            WriteBits(0, 8 - (_nbits % 8));
        }

        // Write remaining bytes
        if (_nbits > 0)
        {
            int bytesToWrite = _nbits / 8;
            byte[] bytes = BitConverter.GetBytes(_buffer);
            if (!BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            _stream.Write(bytes, 0, bytesToWrite);
            _buffer = 0;
            _nbits = 0;
        }
    }

    /// <summary>
    /// Writes a simple huffman tree with a single symbol.
    /// </summary>
    public void WriteSingleEntryHuffmanTree(byte symbol)
    {
        WriteBits(1, 2); // simple code
        if (symbol <= 1)
        {
            WriteBits(0, 1); // 1-bit symbol
            WriteBits(symbol, 1);
        }
        else
        {
            WriteBits(1, 1); // 8-bit symbol
            WriteBits(symbol, 8);
        }
    }
}
