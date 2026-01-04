namespace TinyImage.Codecs.Tiff;

/// <summary>
/// TIFF predictor values for compression preprocessing.
/// Based on image-tiff tags.rs lines 281-293.
/// </summary>
internal enum TiffPredictor : ushort
{
    /// <summary>
    /// No prediction scheme used.
    /// </summary>
    None = 1,

    /// <summary>
    /// Horizontal differencing.
    /// Each pixel value is replaced with the difference from the previous pixel.
    /// </summary>
    Horizontal = 2,

    /// <summary>
    /// Floating point horizontal differencing.
    /// </summary>
    FloatingPoint = 3
}
