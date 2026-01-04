using System;

namespace TinyImage.Codecs.Tiff;

/// <summary>
/// Represents an IFD (Image File Directory) entry.
/// Based on image-tiff decoder/ifd.rs.
/// </summary>
internal readonly struct TiffIfdEntry
{
    /// <summary>
    /// The tag identifier.
    /// </summary>
    public TiffTag Tag { get; }

    /// <summary>
    /// The field type.
    /// </summary>
    public TiffFieldType FieldType { get; }

    /// <summary>
    /// The number of values.
    /// </summary>
    public uint Count { get; }

    /// <summary>
    /// The raw value/offset bytes (4 bytes for TIFF, 8 for BigTIFF).
    /// For values that fit inline, this contains the actual value.
    /// For larger values, this contains an offset to where the data is stored.
    /// </summary>
    public ulong ValueOffset { get; }

    /// <summary>
    /// Creates a new IFD entry.
    /// </summary>
    public TiffIfdEntry(TiffTag tag, TiffFieldType fieldType, uint count, ulong valueOffset)
    {
        Tag = tag;
        FieldType = fieldType;
        Count = count;
        ValueOffset = valueOffset;
    }

    /// <summary>
    /// Gets the total byte size of the value data.
    /// </summary>
    public int ValueByteSize => FieldType.GetByteLength() * (int)Count;

    /// <summary>
    /// Gets whether the value fits inline in the entry (doesn't need offset).
    /// </summary>
    /// <param name="isBigTiff">True if this is a BigTIFF file.</param>
    public bool IsValueInline(bool isBigTiff)
    {
        int maxInline = isBigTiff ? TiffConstants.MaxBigTiffInlineBytes : TiffConstants.MaxInlineBytes;
        return ValueByteSize <= maxInline;
    }

    /// <summary>
    /// Gets the value as a single unsigned integer (for single-value entries).
    /// </summary>
    public ulong GetUInt64Value() => ValueOffset;

    /// <summary>
    /// Gets the value as an unsigned 32-bit integer.
    /// </summary>
    public uint GetUInt32Value() => (uint)ValueOffset;

    /// <summary>
    /// Gets the value as an unsigned 16-bit integer.
    /// </summary>
    public ushort GetUInt16Value() => (ushort)ValueOffset;

    public override string ToString() => $"{Tag} ({FieldType}[{Count}]) = {ValueOffset}";
}
