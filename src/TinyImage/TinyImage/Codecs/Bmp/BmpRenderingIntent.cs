namespace TinyImage.Codecs.Bmp;

/// <summary>
/// Defines the rendering intent for BMP V5 headers.
/// Specifies how to map colors from the image to the output device.
/// </summary>
internal enum BmpRenderingIntent : uint
{
    /// <summary>
    /// No rendering intent specified.
    /// </summary>
    None = 0,

    /// <summary>
    /// Maintains saturation. Used for business graphics where relative
    /// relationships are more important than accurate color reproduction.
    /// </summary>
    Business = 1,

    /// <summary>
    /// Maintains colorimetric match. Used for graphics where accuracy
    /// is important, like logos. Also known as relative colorimetric.
    /// </summary>
    Graphics = 2,

    /// <summary>
    /// Maintains contrast. Used for photographs and images where
    /// preserving appearance is important. Also known as perceptual.
    /// </summary>
    Images = 4,

    /// <summary>
    /// Maintains the white point. Absolute colorimetric intent
    /// that matches colors exactly, including the white point.
    /// </summary>
    AbsoluteColorimetric = 8
}
