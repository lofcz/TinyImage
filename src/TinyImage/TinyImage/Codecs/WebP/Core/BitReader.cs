using System;
using System.IO;

namespace TinyImage.Codecs.WebP.Core;

/// <summary>
/// Bit-level reader for VP8L lossless decoding.
/// Translated from webp-rust lossless.rs:BitReader
/// </summary>
internal class BitReader
{
    private readonly Stream _stream;
    private ulong _buffer;
    private int _nbits;

    public BitReader(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _buffer = 0;
        _nbits = 0;
    }

    public BitReader(byte[] data)
    {
        _stream = new MemoryStream(data);
        _buffer = 0;
        _nbits = 0;
    }

    /// <summary>
    /// Number of bits currently in the buffer.
    /// </summary>
    public int BitsAvailable => _nbits;

    /// <summary>
    /// Fills the buffer with bits from the input stream.
    /// After this function, the buffer will contain up to 56+ bits.
    /// </summary>
    public void Fill()
    {
        while (_nbits < 56)
        {
            int b = _stream.ReadByte();
            if (b < 0)
                break; // EOF

            _buffer |= (ulong)b << _nbits;
            _nbits += 8;
        }
    }

    /// <summary>
    /// Peeks at the next n bits in the buffer without consuming them.
    /// </summary>
    public ulong Peek(int n)
    {
        if (n > 64 || n < 0)
            throw new ArgumentOutOfRangeException(nameof(n));
        if (n == 0)
            return 0;
        return _buffer & ((1UL << n) - 1);
    }

    /// <summary>
    /// Peeks at the full buffer value.
    /// </summary>
    public ulong PeekFull() => _buffer;

    /// <summary>
    /// Consumes n bits from the buffer.
    /// </summary>
    public void Consume(int n)
    {
        if (n > _nbits)
            throw new WebPDecodingException("Bitstream error: not enough bits");

        _buffer >>= n;
        _nbits -= n;
    }

    /// <summary>
    /// Reads n bits from the buffer and returns them as the specified type.
    /// </summary>
    public uint ReadBits(int n)
    {
        if (n > 32 || n < 0)
            throw new ArgumentOutOfRangeException(nameof(n));

        if (_nbits < n)
            Fill();

        if (_nbits < n)
            throw new WebPDecodingException("Bitstream error: unexpected end of stream");

        uint value = (uint)Peek(n);
        Consume(n);
        return value;
    }

    /// <summary>
    /// Reads a single bit.
    /// </summary>
    public bool ReadBit()
    {
        return ReadBits(1) != 0;
    }

    /// <summary>
    /// Reads n bits and returns as byte.
    /// </summary>
    public byte ReadByte(int n)
    {
        if (n > 8)
            throw new ArgumentOutOfRangeException(nameof(n));
        return (byte)ReadBits(n);
    }

    /// <summary>
    /// Reads n bits and returns as ushort.
    /// </summary>
    public ushort ReadUInt16(int n)
    {
        if (n > 16)
            throw new ArgumentOutOfRangeException(nameof(n));
        return (ushort)ReadBits(n);
    }
}
