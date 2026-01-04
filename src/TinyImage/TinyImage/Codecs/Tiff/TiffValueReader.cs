using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace TinyImage.Codecs.Tiff;

/// <summary>
/// Reads tag values from a TIFF stream.
/// Based on image-tiff decoder/tag_reader.rs.
/// </summary>
internal sealed class TiffValueReader
{
    private readonly Stream _stream;
    private readonly TiffByteOrder _byteOrder;
    private readonly bool _isBigTiff;

    /// <summary>
    /// Creates a new value reader.
    /// </summary>
    public TiffValueReader(Stream stream, TiffByteOrder byteOrder, bool isBigTiff)
    {
        _stream = stream;
        _byteOrder = byteOrder;
        _isBigTiff = isBigTiff;
    }

    /// <summary>
    /// Reads an array of unsigned 16-bit integers from an IFD entry.
    /// </summary>
    public ushort[] ReadUInt16Array(TiffIfdEntry entry)
    {
        if (entry.Count == 0)
            return Array.Empty<ushort>();

        var result = new ushort[entry.Count];

        if (entry.IsValueInline(_isBigTiff))
        {
            // For single values, ParseIfdEntry already extracted the value correctly
            // with proper byte order handling, so we can use it directly
            if (entry.Count == 1)
            {
                result[0] = (ushort)entry.ValueOffset;
            }
            else
            {
                // Multiple values packed inline - need to extract from raw bytes
                ReadInlineUInt16Array(entry.ValueOffset, result);
            }
        }
        else
        {
            // Value is at offset
            long currentPos = _stream.Position;
            _stream.Position = (long)entry.ValueOffset;

            var buffer = new byte[entry.Count * 2];
            _stream.Read(buffer, 0, buffer.Length);

            for (int i = 0; i < result.Length; i++)
            {
                result[i] = _byteOrder.ReadUInt16(buffer.AsSpan(i * 2));
            }

            _stream.Position = currentPos;
        }

        return result;
    }

    /// <summary>
    /// Reads an array of unsigned 32-bit integers from an IFD entry.
    /// </summary>
    public uint[] ReadUInt32Array(TiffIfdEntry entry)
    {
        if (entry.Count == 0)
            return Array.Empty<uint>();

        var result = new uint[entry.Count];

        if (entry.IsValueInline(_isBigTiff))
        {
            // Value is inline - extract from ValueOffset
            if (entry.Count == 1)
            {
                result[0] = (uint)entry.ValueOffset;
            }
        }
        else
        {
            // Value is at offset
            long currentPos = _stream.Position;
            _stream.Position = (long)entry.ValueOffset;

            var buffer = new byte[entry.Count * 4];
            _stream.Read(buffer, 0, buffer.Length);

            for (int i = 0; i < result.Length; i++)
            {
                result[i] = _byteOrder.ReadUInt32(buffer.AsSpan(i * 4));
            }

            _stream.Position = currentPos;
        }

        return result;
    }

    /// <summary>
    /// Reads an array of offsets (32-bit for TIFF, 64-bit for BigTIFF).
    /// </summary>
    public long[] ReadOffsetArray(TiffIfdEntry entry)
    {
        if (entry.Count == 0)
            return Array.Empty<long>();

        var result = new long[entry.Count];

        // Check field type to determine reading method
        bool isLong8 = entry.FieldType == TiffFieldType.Long8 || entry.FieldType == TiffFieldType.Ifd8;

        if (entry.IsValueInline(_isBigTiff))
        {
            // Value is inline
            if (entry.Count == 1)
            {
                result[0] = (long)entry.ValueOffset;
            }
            else if (!isLong8)
            {
                // Multiple shorts or longs packed inline
                ReadInlineOffsets(entry.ValueOffset, result, entry.FieldType);
            }
        }
        else
        {
            // Value is at offset
            long currentPos = _stream.Position;
            _stream.Position = (long)entry.ValueOffset;

            int elementSize = isLong8 ? 8 : (entry.FieldType == TiffFieldType.Short ? 2 : 4);
            var buffer = new byte[(int)entry.Count * elementSize];
            _stream.Read(buffer, 0, buffer.Length);

            for (int i = 0; i < result.Length; i++)
            {
                if (isLong8)
                    result[i] = (long)_byteOrder.ReadUInt64(buffer.AsSpan(i * 8));
                else if (entry.FieldType == TiffFieldType.Short)
                    result[i] = _byteOrder.ReadUInt16(buffer.AsSpan(i * 2));
                else
                    result[i] = _byteOrder.ReadUInt32(buffer.AsSpan(i * 4));
            }

            _stream.Position = currentPos;
        }

        return result;
    }

    /// <summary>
    /// Reads a string value from an IFD entry.
    /// </summary>
    public string ReadString(TiffIfdEntry entry)
    {
        if (entry.Count == 0)
            return string.Empty;

        byte[] buffer;

        if (entry.IsValueInline(_isBigTiff))
        {
            // Value is inline
            int len = Math.Min((int)entry.Count, _isBigTiff ? 8 : 4);
            buffer = new byte[len];
            var valueBytes = BitConverter.GetBytes(entry.ValueOffset);
            if (!_byteOrder.IsLittleEndian)
                Array.Reverse(valueBytes);
            Array.Copy(valueBytes, buffer, len);
        }
        else
        {
            // Value is at offset
            long currentPos = _stream.Position;
            _stream.Position = (long)entry.ValueOffset;

            buffer = new byte[entry.Count];
            _stream.Read(buffer, 0, buffer.Length);

            _stream.Position = currentPos;
        }

        // Remove null terminator if present
        int length = Array.IndexOf(buffer, (byte)0);
        if (length < 0) length = buffer.Length;

        return Encoding.ASCII.GetString(buffer, 0, length);
    }

    /// <summary>
    /// Reads raw bytes from an IFD entry.
    /// </summary>
    public byte[] ReadBytes(TiffIfdEntry entry)
    {
        if (entry.Count == 0)
            return Array.Empty<byte>();

        byte[] buffer;

        if (entry.IsValueInline(_isBigTiff))
        {
            int len = Math.Min((int)entry.Count, _isBigTiff ? 8 : 4);
            buffer = new byte[len];
            var valueBytes = BitConverter.GetBytes(entry.ValueOffset);
            if (!_byteOrder.IsLittleEndian)
                Array.Reverse(valueBytes);
            Array.Copy(valueBytes, buffer, len);
        }
        else
        {
            long currentPos = _stream.Position;
            _stream.Position = (long)entry.ValueOffset;

            buffer = new byte[entry.Count];
            _stream.Read(buffer, 0, buffer.Length);

            _stream.Position = currentPos;
        }

        return buffer;
    }

    /// <summary>
    /// Reads a byte array from an IFD entry.
    /// </summary>
    public byte[] ReadByteArray(TiffIfdEntry entry) => ReadBytes(entry);

    /// <summary>
    /// Reads an array of RATIONAL values (numerator/denominator pairs) as floats.
    /// </summary>
    public float[] ReadRationalArray(TiffIfdEntry entry)
    {
        if (entry.Count == 0)
            return Array.Empty<float>();

        var result = new float[entry.Count];

        // Each RATIONAL is 8 bytes (2 x 4-byte integers)
        long currentPos = _stream.Position;
        _stream.Position = (long)entry.ValueOffset;

        var buffer = new byte[(int)entry.Count * 8];
        _stream.Read(buffer, 0, buffer.Length);

        for (int i = 0; i < result.Length; i++)
        {
            uint numerator = _byteOrder.ReadUInt32(buffer.AsSpan(i * 8));
            uint denominator = _byteOrder.ReadUInt32(buffer.AsSpan(i * 8 + 4));
            result[i] = denominator != 0 ? (float)numerator / denominator : 0f;
        }

        _stream.Position = currentPos;
        return result;
    }

    private void ReadInlineUInt16Array(ulong valueOffset, ushort[] result)
    {
        // valueOffset contains the raw bytes that were read using _byteOrder.ReadUInt32/ReadUInt64
        // After BitConverter.GetBytes, the bytes are in native byte order
        // We need to extract the values correctly based on how they were packed
        var bytes = BitConverter.GetBytes(valueOffset);
        
        int maxCount = Math.Min(result.Length, (_isBigTiff ? 8 : 4) / 2);
        
        if (_byteOrder.IsLittleEndian)
        {
            // Little-endian TIFF: shorts are in natural order in the byte array
            for (int i = 0; i < maxCount; i++)
            {
                result[i] = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(i * 2));
            }
        }
        else
        {
            // Big-endian TIFF: shorts are in reverse order after ReadUInt32/ReadUInt64 + BitConverter
            // The first short ends up at the highest byte positions
            int totalShorts = (_isBigTiff ? 8 : 4) / 2;
            for (int i = 0; i < maxCount; i++)
            {
                int byteOffset = (totalShorts - 1 - i) * 2;
                result[i] = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(byteOffset));
            }
        }
    }

    private void ReadInlineOffsets(ulong valueOffset, long[] result, TiffFieldType fieldType)
    {
        var bytes = BitConverter.GetBytes(valueOffset);
        if (!_byteOrder.IsLittleEndian)
            Array.Reverse(bytes);

        if (fieldType == TiffFieldType.Short)
        {
            int maxCount = Math.Min(result.Length, (_isBigTiff ? 8 : 4) / 2);
            for (int i = 0; i < maxCount; i++)
            {
                result[i] = _byteOrder.ReadUInt16(bytes.AsSpan(i * 2));
            }
        }
        else
        {
            int maxCount = Math.Min(result.Length, (_isBigTiff ? 8 : 4) / 4);
            for (int i = 0; i < maxCount; i++)
            {
                result[i] = _byteOrder.ReadUInt32(bytes.AsSpan(i * 4));
            }
        }
    }
}
