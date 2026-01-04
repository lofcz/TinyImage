namespace TinyImage;

/// <summary>
/// Specifies the interpolation algorithm used for resizing images.
/// </summary>
public enum ResizeMode
{
    /// <summary>
    /// Nearest-neighbor interpolation.
    /// Fastest, but produces pixelated results.
    /// </summary>
    NearestNeighbor,

    /// <summary>
    /// Bilinear interpolation.
    /// Good balance between quality and speed. This is the default.
    /// </summary>
    Bilinear,

    /// <summary>
    /// Bicubic interpolation.
    /// Higher quality results, slower than bilinear.
    /// </summary>
    Bicubic
}
