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
    Gif,

    /// <summary>
    /// JPEG 2000 format (.jp2, .j2k, .j2c).
    /// Supports both lossless and lossy compression with alpha channel.
    /// Based on ISO/IEC 15444-1 (JPEG 2000 Part 1).
    /// </summary>
    Jpeg2000,

    /// <summary>
    /// BMP format (Bitmap).
    /// Uncompressed or RLE compressed format with support for 1-32 bits per pixel.
    /// </summary>
    Bmp
}
