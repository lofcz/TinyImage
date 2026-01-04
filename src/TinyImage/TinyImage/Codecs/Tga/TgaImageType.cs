namespace TinyImage.Codecs.Tga;

/// <summary>
/// Specifies the type of image data in a TGA file.
/// </summary>
internal enum TgaImageType : byte
{
    /// <summary>
    /// No image data included.
    /// </summary>
    NoData = 0,

    /// <summary>
    /// Uncompressed, color-mapped image.
    /// </summary>
    UncompressedColorMapped = 1,

    /// <summary>
    /// Uncompressed, true-color image.
    /// </summary>
    UncompressedTrueColor = 2,

    /// <summary>
    /// Uncompressed, grayscale image.
    /// </summary>
    UncompressedGrayscale = 3,

    /// <summary>
    /// Run-length encoded, color-mapped image.
    /// </summary>
    RleColorMapped = 9,

    /// <summary>
    /// Run-length encoded, true-color image.
    /// </summary>
    RleTrueColor = 10,

    /// <summary>
    /// Run-length encoded, grayscale image.
    /// </summary>
    RleGrayscale = 11
}
