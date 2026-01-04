namespace TinyImage.Codecs.Bmp;

/// <summary>
/// Defines the compression type of the image data in the bitmap file.
/// </summary>
internal enum BmpCompression : int
{
    /// <summary>
    /// No compression. Each image row has a multiple of four elements.
    /// If the row has less elements, zeros will be added at the right side.
    /// The format depends on the number of bits stored in the info header.
    /// </summary>
    RGB = 0,

    /// <summary>
    /// Run-length encoding for 8-bit images.
    /// Two bytes are one data record. If the first byte is not zero, the
    /// next byte will be repeated as much as the value of the first byte.
    /// If the first byte is zero, the record has different meanings depending
    /// on the second byte. If the second byte is zero, it is the end of the row,
    /// if it is one, it is the end of the image.
    /// </summary>
    RLE8 = 1,

    /// <summary>
    /// Run-length encoding for 4-bit images.
    /// Two bytes are one data record. If the first byte is not zero, the
    /// next two half bytes will be repeated as much as the value of the first byte.
    /// If the first byte is zero, the record has different meanings depending
    /// on the second byte.
    /// </summary>
    RLE4 = 2,

    /// <summary>
    /// Uncompressed with bit field masks.
    /// Each image row has a multiple of four elements. Color masks define
    /// how to extract RGB values from each pixel.
    /// </summary>
    BitFields = 3,

    /// <summary>
    /// The bitmap contains a JPEG image. Not supported.
    /// </summary>
    JPEG = 4,

    /// <summary>
    /// The bitmap contains a PNG image. Not supported.
    /// </summary>
    PNG = 5,

    /// <summary>
    /// Introduced with Windows CE.
    /// Specifies that the bitmap is not compressed and that the color table
    /// consists of four DWORD color masks that specify the red, green, blue,
    /// and alpha components of each pixel.
    /// </summary>
    AlphaBitFields = 6,

    /// <summary>
    /// Windows CMYK - not commonly supported.
    /// </summary>
    CMYK = 11,

    /// <summary>
    /// Windows CMYK with RLE8 compression - not commonly supported.
    /// </summary>
    CMYKRLE8 = 12,

    /// <summary>
    /// Windows CMYK with RLE4 compression - not commonly supported.
    /// </summary>
    CMYKRLE4 = 13,

    /// <summary>
    /// OS/2 1-bit Huffman 1-D compression (ITU-T T.4/G3 fax style).
    /// Only valid for 1-bit monochrome images.
    /// Note: In OS/2, this uses value 3 (same as Windows BI_BITFIELDS).
    /// Remapped to 101 to avoid conflict.
    /// </summary>
    OS2Huffman = 101,

    /// <summary>
    /// OS/2 specific compression type.
    /// Similar to run length encoding but run values are three bytes (RGB).
    /// Note: In OS/2, this uses value 4 (same as Windows BI_JPEG).
    /// Remapped to 100 to avoid conflict.
    /// </summary>
    RLE24 = 100
}
