namespace TinyImage.Codecs.Tiff;

/// <summary>
/// TIFF resolution unit values.
/// Based on image-tiff tags.rs lines 295-302.
/// </summary>
internal enum TiffResolutionUnit : ushort
{
    /// <summary>
    /// No absolute unit of measurement.
    /// </summary>
    None = 1,

    /// <summary>
    /// Inch.
    /// </summary>
    Inch = 2,

    /// <summary>
    /// Centimeter.
    /// </summary>
    Centimeter = 3
}
