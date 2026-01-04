namespace TinyImage;

/// <summary>
/// Specifies the image format for loading and saving.
/// </summary>
public enum ImageFormat
{
    /// <summary>
    /// PNG format (Portable Network Graphics).
    /// Lossless compression with alpha channel support.
    /// </summary>
    Png,

    /// <summary>
    /// JPEG format (Joint Photographic Experts Group).
    /// Lossy compression, no alpha channel support.
    /// </summary>
    Jpeg,

    /// <summary>
    /// GIF format (Graphics Interchange Format).
    /// Supports animation and transparency, limited to 256 colors per frame.
    /// </summary>
    Gif
}
