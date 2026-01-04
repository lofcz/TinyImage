namespace TinyImage.Codecs.Tiff;

/// <summary>
/// TIFF photometric interpretation values.
/// Based on image-tiff tags.rs lines 259-272.
/// </summary>
internal enum TiffPhotometric : ushort
{
    /// <summary>
    /// For bilevel and grayscale images: 0 is imaged as white.
    /// </summary>
    WhiteIsZero = 0,

    /// <summary>
    /// For bilevel and grayscale images: 0 is imaged as black.
    /// </summary>
    BlackIsZero = 1,

    /// <summary>
    /// RGB color model.
    /// </summary>
    Rgb = 2,

    /// <summary>
    /// Palette color (indexed color).
    /// </summary>
    Palette = 3,

    /// <summary>
    /// Transparency mask.
    /// </summary>
    TransparencyMask = 4,

    /// <summary>
    /// CMYK color model.
    /// </summary>
    Cmyk = 5,

    /// <summary>
    /// YCbCr color model.
    /// </summary>
    YCbCr = 6,

    /// <summary>
    /// CIE L*a*b* color model.
    /// </summary>
    CieLab = 8,

    /// <summary>
    /// ICC L*a*b* color model.
    /// </summary>
    IccLab = 9,

    /// <summary>
    /// ITU L*a*b* color model.
    /// </summary>
    ItuLab = 10
}
