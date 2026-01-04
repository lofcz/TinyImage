namespace TinyImage.Codecs.Tiff;

/// <summary>
/// Constants used in TIFF encoding and decoding.
/// </summary>
internal static class TiffConstants
{
    /// <summary>
    /// Little-endian byte order marker ("II").
    /// </summary>
    public const ushort LittleEndianMarker = 0x4949;

    /// <summary>
    /// Big-endian byte order marker ("MM").
    /// </summary>
    public const ushort BigEndianMarker = 0x4D4D;

    /// <summary>
    /// TIFF magic number.
    /// </summary>
    public const ushort TiffMagic = 42;

    /// <summary>
    /// BigTIFF magic number.
    /// </summary>
    public const ushort BigTiffMagic = 43;

    /// <summary>
    /// Size of a standard TIFF IFD entry in bytes.
    /// </summary>
    public const int IfdEntrySize = 12;

    /// <summary>
    /// Size of a BigTIFF IFD entry in bytes.
    /// </summary>
    public const int BigTiffIfdEntrySize = 20;

    /// <summary>
    /// Maximum value count that fits inline in a standard TIFF IFD entry (4 bytes).
    /// </summary>
    public const int MaxInlineBytes = 4;

    /// <summary>
    /// Maximum value count that fits inline in a BigTIFF IFD entry (8 bytes).
    /// </summary>
    public const int MaxBigTiffInlineBytes = 8;

    /// <summary>
    /// Default rows per strip if not specified.
    /// </summary>
    public const uint DefaultRowsPerStrip = uint.MaxValue;

    /// <summary>
    /// Default strip size target in bytes (for encoder).
    /// </summary>
    public const int DefaultStripSizeTarget = 8192;
}
