namespace TinyImage.Codecs.Tiff;

/// <summary>
/// TIFF compression methods.
/// Based on image-tiff tags.rs lines 235-257.
/// </summary>
internal enum TiffCompression : ushort
{
    /// <summary>
    /// Unspecified (treated as no compression).
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// No compression.
    /// </summary>
    None = 1,

    /// <summary>
    /// CCITT Group 3 1-Dimensional Modified Huffman run length encoding.
    /// </summary>
    Huffman = 2,

    /// <summary>
    /// CCITT T.4 bi-level encoding (Fax Group 3).
    /// </summary>
    Fax3 = 3,

    /// <summary>
    /// CCITT T.6 bi-level encoding (Fax Group 4).
    /// </summary>
    Fax4 = 4,

    /// <summary>
    /// LZW compression.
    /// </summary>
    Lzw = 5,

    /// <summary>
    /// Old-style JPEG compression (obsolete).
    /// </summary>
    OldJpeg = 6,

    /// <summary>
    /// JPEG compression (new style).
    /// </summary>
    Jpeg = 7,

    /// <summary>
    /// Deflate compression (Adobe style).
    /// </summary>
    Deflate = 8,

    /// <summary>
    /// PackBits compression.
    /// </summary>
    PackBits = 32773,

    /// <summary>
    /// Deflate compression (old style, same as 8).
    /// </summary>
    OldDeflate = 32946,

    /// <summary>
    /// Zstandard compression.
    /// </summary>
    Zstd = 50000
}
