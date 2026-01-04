using System;
using System.Buffers.Binary;

namespace TinyImage.Codecs.Bmp;

/// <summary>
/// Stores general information about the Bitmap file.
/// The first two bytes are stored in big-endian order (type marker),
/// all other integer values are stored in little-endian format.
/// </summary>
internal readonly struct BmpFileHeader
{
    /// <summary>
    /// Size of the file header in bytes.
    /// </summary>
    public const int Size = 14;

    /// <summary>
    /// Gets the Bitmap identifier (type marker).
    /// The field used to identify the bitmap file: 0x42 0x4D (ASCII "BM").
    /// </summary>
    public ushort Type { get; }

    /// <summary>
    /// Gets the size of the bitmap file in bytes.
    /// </summary>
    public int FileSize { get; }

    /// <summary>
    /// Gets the first reserved value. Actual value depends on the application.
    /// </summary>
    public ushort Reserved1 { get; }

    /// <summary>
    /// Gets the second reserved value. Actual value depends on the application.
    /// </summary>
    public ushort Reserved2 { get; }

    /// <summary>
    /// Gets the offset (starting address) of the byte where the bitmap pixel data begins.
    /// </summary>
    public int PixelDataOffset { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="BmpFileHeader"/> struct.
    /// </summary>
    public BmpFileHeader(ushort type, int fileSize, ushort reserved1, ushort reserved2, int pixelDataOffset)
    {
        Type = type;
        FileSize = fileSize;
        Reserved1 = reserved1;
        Reserved2 = reserved2;
        PixelDataOffset = pixelDataOffset;
    }

    /// <summary>
    /// Parses a BMP file header from the given data.
    /// </summary>
    /// <param name="data">The raw header bytes (at least 14 bytes).</param>
    /// <returns>The parsed file header.</returns>
    public static BmpFileHeader Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < Size)
            throw new ArgumentException($"Data must be at least {Size} bytes.", nameof(data));

        return new BmpFileHeader(
            type: BinaryPrimitives.ReadUInt16LittleEndian(data),
            fileSize: BinaryPrimitives.ReadInt32LittleEndian(data.Slice(2)),
            reserved1: BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(6)),
            reserved2: BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(8)),
            pixelDataOffset: BinaryPrimitives.ReadInt32LittleEndian(data.Slice(10))
        );
    }

    /// <summary>
    /// Writes this file header to the given buffer.
    /// </summary>
    /// <param name="buffer">The buffer to write to (at least 14 bytes).</param>
    public void WriteTo(Span<byte> buffer)
    {
        if (buffer.Length < Size)
            throw new ArgumentException($"Buffer must be at least {Size} bytes.", nameof(buffer));

        BinaryPrimitives.WriteUInt16LittleEndian(buffer, Type);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(2), FileSize);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(6), Reserved1);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(8), Reserved2);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(10), PixelDataOffset);
    }

    /// <summary>
    /// Validates that this is a valid BMP file header.
    /// </summary>
    /// <returns>True if the type marker is valid.</returns>
    public bool IsValid => Type == BmpConstants.TypeMarkers.Bitmap ||
                          Type == BmpConstants.TypeMarkers.BitmapArray ||
                          Type == BmpConstants.TypeMarkers.ColorIcon ||
                          Type == BmpConstants.TypeMarkers.ColorPointer ||
                          Type == BmpConstants.TypeMarkers.Icon ||
                          Type == BmpConstants.TypeMarkers.Pointer;
}
