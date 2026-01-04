using System;
using System.Buffers.Binary;
using System.IO;

namespace TinyImage.Codecs.Tiff;

/// <summary>
/// Provides endianness-aware binary reading and writing for TIFF files.
/// Based on image-tiff decoder/stream.rs.
/// </summary>
internal sealed class TiffByteOrder
{
    private readonly bool _isLittleEndian;

    /// <summary>
    /// Creates a new TiffByteOrder instance.
    /// </summary>
    /// <param name="isLittleEndian">True for little-endian, false for big-endian.</param>
    public TiffByteOrder(bool isLittleEndian)
    {
        _isLittleEndian = isLittleEndian;
    }

    /// <summary>
    /// Gets whether this instance uses little-endian byte order.
    /// </summary>
    public bool IsLittleEndian => _isLittleEndian;

    /// <summary>
    /// Reads an unsigned 16-bit integer from a span.
    /// </summary>
    public ushort ReadUInt16(ReadOnlySpan<byte> data)
    {
        return _isLittleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(data)
            : BinaryPrimitives.ReadUInt16BigEndian(data);
    }

    /// <summary>
    /// Reads a signed 16-bit integer from a span.
    /// </summary>
    public short ReadInt16(ReadOnlySpan<byte> data)
    {
        return _isLittleEndian
            ? BinaryPrimitives.ReadInt16LittleEndian(data)
            : BinaryPrimitives.ReadInt16BigEndian(data);
    }

    /// <summary>
    /// Reads an unsigned 32-bit integer from a span.
    /// </summary>
    public uint ReadUInt32(ReadOnlySpan<byte> data)
    {
        return _isLittleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(data)
            : BinaryPrimitives.ReadUInt32BigEndian(data);
    }

    /// <summary>
    /// Reads a signed 32-bit integer from a span.
    /// </summary>
    public int ReadInt32(ReadOnlySpan<byte> data)
    {
        return _isLittleEndian
            ? BinaryPrimitives.ReadInt32LittleEndian(data)
            : BinaryPrimitives.ReadInt32BigEndian(data);
    }

    /// <summary>
    /// Reads an unsigned 64-bit integer from a span.
    /// </summary>
    public ulong ReadUInt64(ReadOnlySpan<byte> data)
    {
        return _isLittleEndian
            ? BinaryPrimitives.ReadUInt64LittleEndian(data)
            : BinaryPrimitives.ReadUInt64BigEndian(data);
    }

    /// <summary>
    /// Reads a signed 64-bit integer from a span.
    /// </summary>
    public long ReadInt64(ReadOnlySpan<byte> data)
    {
        return _isLittleEndian
            ? BinaryPrimitives.ReadInt64LittleEndian(data)
            : BinaryPrimitives.ReadInt64BigEndian(data);
    }

    /// <summary>
    /// Reads a 32-bit floating point value from a span.
    /// </summary>
    public float ReadSingle(ReadOnlySpan<byte> data)
    {
        var bits = ReadUInt32(data);
        return BitConverterHelper.Int32BitsToSingle((int)bits);
    }

    /// <summary>
    /// Reads a 64-bit floating point value from a span.
    /// </summary>
    public double ReadDouble(ReadOnlySpan<byte> data)
    {
        var bits = ReadInt64(data);
        return BitConverter.Int64BitsToDouble(bits);
    }

    /// <summary>
    /// Writes an unsigned 16-bit integer to a span.
    /// </summary>
    public void WriteUInt16(Span<byte> data, ushort value)
    {
        if (_isLittleEndian)
            BinaryPrimitives.WriteUInt16LittleEndian(data, value);
        else
            BinaryPrimitives.WriteUInt16BigEndian(data, value);
    }

    /// <summary>
    /// Writes an unsigned 32-bit integer to a span.
    /// </summary>
    public void WriteUInt32(Span<byte> data, uint value)
    {
        if (_isLittleEndian)
            BinaryPrimitives.WriteUInt32LittleEndian(data, value);
        else
            BinaryPrimitives.WriteUInt32BigEndian(data, value);
    }

    /// <summary>
    /// Writes an unsigned 64-bit integer to a span.
    /// </summary>
    public void WriteUInt64(Span<byte> data, ulong value)
    {
        if (_isLittleEndian)
            BinaryPrimitives.WriteUInt64LittleEndian(data, value);
        else
            BinaryPrimitives.WriteUInt64BigEndian(data, value);
    }

    /// <summary>
    /// Reads an unsigned 16-bit integer from a stream.
    /// </summary>
    public ushort ReadUInt16(Stream stream)
    {
        var buffer = new byte[2];
        if (ReadFully(stream, buffer, 0, 2) != 2)
            throw new EndOfStreamException();
        return ReadUInt16(buffer);
    }

    /// <summary>
    /// Reads an unsigned 32-bit integer from a stream.
    /// </summary>
    public uint ReadUInt32(Stream stream)
    {
        var buffer = new byte[4];
        if (ReadFully(stream, buffer, 0, 4) != 4)
            throw new EndOfStreamException();
        return ReadUInt32(buffer);
    }

    /// <summary>
    /// Reads an unsigned 64-bit integer from a stream.
    /// </summary>
    public ulong ReadUInt64(Stream stream)
    {
        var buffer = new byte[8];
        if (ReadFully(stream, buffer, 0, 8) != 8)
            throw new EndOfStreamException();
        return ReadUInt64(buffer);
    }

    /// <summary>
    /// Writes an unsigned 16-bit integer to a stream.
    /// </summary>
    public void WriteUInt16(Stream stream, ushort value)
    {
        var buffer = new byte[2];
        WriteUInt16(buffer, value);
        stream.Write(buffer, 0, 2);
    }

    /// <summary>
    /// Writes an unsigned 32-bit integer to a stream.
    /// </summary>
    public void WriteUInt32(Stream stream, uint value)
    {
        var buffer = new byte[4];
        WriteUInt32(buffer, value);
        stream.Write(buffer, 0, 4);
    }

    /// <summary>
    /// Writes an unsigned 64-bit integer to a stream.
    /// </summary>
    public void WriteUInt64(Stream stream, ulong value)
    {
        var buffer = new byte[8];
        WriteUInt64(buffer, value);
        stream.Write(buffer, 0, 8);
    }

    /// <summary>
    /// Converts buffer endianness from file byte order to native byte order.
    /// </summary>
    /// <param name="buffer">The buffer to convert.</param>
    /// <param name="elementSize">Size of each element (1, 2, 4, or 8 bytes).</param>
    public void ConvertToNative(Span<byte> buffer, int elementSize)
    {
        // If file is same endianness as native, nothing to do
        if (_isLittleEndian == BitConverter.IsLittleEndian)
            return;

        // Swap bytes for each element
        switch (elementSize)
        {
            case 1:
                // No conversion needed for single bytes
                break;
            case 2:
                for (int i = 0; i < buffer.Length - 1; i += 2)
                {
                    (buffer[i], buffer[i + 1]) = (buffer[i + 1], buffer[i]);
                }
                break;
            case 4:
                for (int i = 0; i < buffer.Length - 3; i += 4)
                {
                    (buffer[i], buffer[i + 3]) = (buffer[i + 3], buffer[i]);
                    (buffer[i + 1], buffer[i + 2]) = (buffer[i + 2], buffer[i + 1]);
                }
                break;
            case 8:
                for (int i = 0; i < buffer.Length - 7; i += 8)
                {
                    (buffer[i], buffer[i + 7]) = (buffer[i + 7], buffer[i]);
                    (buffer[i + 1], buffer[i + 6]) = (buffer[i + 6], buffer[i + 1]);
                    (buffer[i + 2], buffer[i + 5]) = (buffer[i + 5], buffer[i + 2]);
                    (buffer[i + 3], buffer[i + 4]) = (buffer[i + 4], buffer[i + 3]);
                }
                break;
        }
    }

    /// <summary>
    /// Reads exactly count bytes from stream, handling partial reads.
    /// </summary>
    private static int ReadFully(Stream stream, byte[] buffer, int offset, int count)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = stream.Read(buffer, offset + totalRead, count - totalRead);
            if (read == 0)
                break;
            totalRead += read;
        }
        return totalRead;
    }
}

/// <summary>
/// Helper for BitConverter methods not available in netstandard2.0.
/// </summary>
internal static class BitConverterHelper
{
    public static unsafe float Int32BitsToSingle(int value)
    {
        return *(float*)&value;
    }

    public static unsafe int SingleToInt32Bits(float value)
    {
        return *(int*)&value;
    }
}
