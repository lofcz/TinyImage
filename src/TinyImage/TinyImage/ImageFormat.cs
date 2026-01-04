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
    Bmp,

    /// <summary>
    /// PBM format (Portable Bitmap).
    /// Black and white images. Supports P1 (ASCII) and P4 (binary) variants.
    /// </summary>
    Pbm,

    /// <summary>
    /// PGM format (Portable Graymap).
    /// Grayscale images. Supports P2 (ASCII) and P5 (binary) variants.
    /// </summary>
    Pgm,

    /// <summary>
    /// PPM format (Portable Pixmap).
    /// RGB color images. Supports P3 (ASCII) and P6 (binary) variants.
    /// </summary>
    Ppm,

    /// <summary>
    /// WebP format.
    /// Supports both lossy (VP8) and lossless (VP8L) compression with alpha channel.
    /// Also supports animation.
    /// </summary>
    WebP,

    /// <summary>
    /// TIFF format (Tagged Image File Format).
    /// Supports various compression methods (none, LZW, Deflate, PackBits),
    /// multiple pages, strips, and tiles.
    /// </summary>
    Tiff
}
