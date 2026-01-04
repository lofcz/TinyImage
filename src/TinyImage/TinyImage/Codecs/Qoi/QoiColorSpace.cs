namespace TinyImage.Codecs.Qoi;

/// <summary>
/// Specifies the color space used in a QOI image.
/// </summary>
/// <remarks>
/// The color space is purely informative and does not affect
/// how pixels are encoded or decoded. It indicates how the
/// color channels should be interpreted by applications.
/// </remarks>
internal enum QoiColorSpace : byte
{
    /// <summary>
    /// sRGB color space with linear alpha channel.
    /// RGB channels are gamma-corrected, alpha is linear.
    /// </summary>
    SRgb = 0,

    /// <summary>
    /// All channels are linear (not gamma-corrected).
    /// </summary>
    Linear = 1
}
