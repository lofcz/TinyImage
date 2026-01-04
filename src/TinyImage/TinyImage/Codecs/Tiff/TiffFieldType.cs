namespace TinyImage.Codecs.Tiff;

/// <summary>
/// TIFF IFD field (tag value) types.
/// Based on image-tiff tags.rs lines 158-193.
/// </summary>
internal enum TiffFieldType : ushort
{
    /// <summary>
    /// 8-bit unsigned integer.
    /// </summary>
    Byte = 1,

    /// <summary>
    /// 8-bit byte containing a 7-bit ASCII code (null-terminated).
    /// </summary>
    Ascii = 2,

    /// <summary>
    /// 16-bit unsigned integer.
    /// </summary>
    Short = 3,

    /// <summary>
    /// 32-bit unsigned integer.
    /// </summary>
    Long = 4,

    /// <summary>
    /// Two LONGs: numerator and denominator of a fraction.
    /// </summary>
    Rational = 5,

    /// <summary>
    /// 8-bit signed integer.
    /// </summary>
    SByte = 6,

    /// <summary>
    /// 8-bit byte that may contain anything.
    /// </summary>
    Undefined = 7,

    /// <summary>
    /// 16-bit signed integer.
    /// </summary>
    SShort = 8,

    /// <summary>
    /// 32-bit signed integer.
    /// </summary>
    SLong = 9,

    /// <summary>
    /// Two SLONGs: numerator and denominator of a fraction.
    /// </summary>
    SRational = 10,

    /// <summary>
    /// 32-bit IEEE floating point.
    /// </summary>
    Float = 11,

    /// <summary>
    /// 64-bit IEEE floating point.
    /// </summary>
    Double = 12,

    /// <summary>
    /// 32-bit unsigned integer (IFD offset).
    /// </summary>
    Ifd = 13,

    /// <summary>
    /// BigTIFF 64-bit unsigned integer.
    /// </summary>
    Long8 = 16,

    /// <summary>
    /// BigTIFF 64-bit signed integer.
    /// </summary>
    SLong8 = 17,

    /// <summary>
    /// BigTIFF 64-bit unsigned integer (IFD offset).
    /// </summary>
    Ifd8 = 18
}

/// <summary>
/// Extension methods for TiffFieldType.
/// </summary>
internal static class TiffFieldTypeExtensions
{
    /// <summary>
    /// Gets the byte length of a single value of this field type.
    /// </summary>
    public static int GetByteLength(this TiffFieldType type) => type switch
    {
        TiffFieldType.Byte => 1,
        TiffFieldType.Ascii => 1,
        TiffFieldType.Short => 2,
        TiffFieldType.Long => 4,
        TiffFieldType.Rational => 8,
        TiffFieldType.SByte => 1,
        TiffFieldType.Undefined => 1,
        TiffFieldType.SShort => 2,
        TiffFieldType.SLong => 4,
        TiffFieldType.SRational => 8,
        TiffFieldType.Float => 4,
        TiffFieldType.Double => 8,
        TiffFieldType.Ifd => 4,
        TiffFieldType.Long8 => 8,
        TiffFieldType.SLong8 => 8,
        TiffFieldType.Ifd8 => 8,
        _ => 1
    };
}
