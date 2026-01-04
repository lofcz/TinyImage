namespace TinyImage.Codecs.Tiff;

/// <summary>
/// TIFF planar configuration values.
/// Based on image-tiff tags.rs lines 274-279.
/// </summary>
internal enum TiffPlanarConfig : ushort
{
    /// <summary>
    /// Chunky format: component values are stored contiguously (RGBRGBRGB...).
    /// </summary>
    Chunky = 1,

    /// <summary>
    /// Planar format: component values are stored in separate planes (RRR...GGG...BBB...).
    /// </summary>
    Planar = 2
}
