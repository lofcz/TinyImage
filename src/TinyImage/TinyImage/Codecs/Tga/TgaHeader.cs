using System;
using System.Buffers.Binary;

namespace TinyImage.Codecs.Tga;

/// <summary>
/// Represents the 18-byte TGA file header.
/// </summary>
internal sealed class TgaHeader
{
    /// <summary>
    /// Size of the TGA header in bytes.
    /// </summary>
    public const int Size = 18;

    /// <summary>
    /// Number of bytes in the Image ID field (0-255).
    /// </summary>
    public byte IdLength { get; set; }

    /// <summary>
    /// Type of color map (0 = no color map, 1 = has color map).
    /// </summary>
    public byte ColorMapType { get; set; }

    /// <summary>
    /// Type of image data.
    /// </summary>
    public TgaImageType ImageType { get; set; }

    /// <summary>
    /// Index of the first color map entry.
    /// </summary>
    public ushort ColorMapOrigin { get; set; }

    /// <summary>
    /// Total number of color map entries.
    /// </summary>
    public ushort ColorMapLength { get; set; }

    /// <summary>
    /// Number of bits per color map entry (15, 16, 24, or 32).
    /// </summary>
    public byte ColorMapDepth { get; set; }

    /// <summary>
    /// X-origin of the image (lower left corner).
    /// </summary>
    public ushort XOrigin { get; set; }

    /// <summary>
    /// Y-origin of the image (lower left corner).
    /// </summary>
    public ushort YOrigin { get; set; }

    /// <summary>
    /// Width of the image in pixels.
    /// </summary>
    public ushort Width { get; set; }

    /// <summary>
    /// Height of the image in pixels.
    /// </summary>
    public ushort Height { get; set; }

    /// <summary>
    /// Number of bits per pixel (8, 16, 24, or 32).
    /// </summary>
    public byte PixelDepth { get; set; }

    /// <summary>
    /// Image descriptor byte containing alpha bits count and origin.
    /// Bits 0-3: Number of attribute (alpha) bits per pixel.
    /// Bits 4-5: Screen origin (see TgaOrientation).
    /// Bits 6-7: Must be zero.
    /// </summary>
    public byte ImageDescriptor { get; set; }

    /// <summary>
    /// Gets whether this image has a color map.
    /// </summary>
    public bool HasColorMap => ColorMapType == 1;

    /// <summary>
    /// Gets whether this image uses RLE compression.
    /// </summary>
    public bool IsCompressed => ImageType == TgaImageType.RleColorMapped ||
                                 ImageType == TgaImageType.RleTrueColor ||
                                 ImageType == TgaImageType.RleGrayscale;

    /// <summary>
    /// Gets whether this image is grayscale.
    /// </summary>
    public bool IsGrayscale => ImageType == TgaImageType.UncompressedGrayscale ||
                                ImageType == TgaImageType.RleGrayscale;

    /// <summary>
    /// Gets whether this image is color-mapped (paletted).
    /// </summary>
    public bool IsColorMapped => ImageType == TgaImageType.UncompressedColorMapped ||
                                  ImageType == TgaImageType.RleColorMapped;

    /// <summary>
    /// Gets the screen origin (pixel ordering) of the image.
    /// </summary>
    public TgaOrientation Orientation => (TgaOrientation)((ImageDescriptor >> 4) & 0x03);

    /// <summary>
    /// Gets the number of alpha/attribute bits per pixel.
    /// </summary>
    public int AlphaBits => ImageDescriptor & 0x0F;

    /// <summary>
    /// Gets the number of bytes per pixel.
    /// </summary>
    public int BytesPerPixel => PixelDepth / 8;

    /// <summary>
    /// Gets the number of bytes per color map entry.
    /// </summary>
    public int ColorMapEntryBytes => ColorMapDepth / 8;

    /// <summary>
    /// Parses a TGA header from the given byte span.
    /// </summary>
    /// <param name="data">The header data (must be at least 18 bytes).</param>
    /// <returns>The parsed header.</returns>
    public static TgaHeader Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < Size)
            throw new ArgumentException($"Header data must be at least {Size} bytes.", nameof(data));

        var header = new TgaHeader
        {
            IdLength = data[0],
            ColorMapType = data[1],
            ImageType = (TgaImageType)data[2],
            ColorMapOrigin = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(3)),
            ColorMapLength = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5)),
            ColorMapDepth = data[7],
            XOrigin = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(8)),
            YOrigin = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(10)),
            Width = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(12)),
            Height = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(14)),
            PixelDepth = data[16],
            ImageDescriptor = data[17]
        };

        // Validate image type
        if (!IsValidImageType(header.ImageType))
            throw new InvalidOperationException($"Invalid TGA image type: {(byte)header.ImageType}");

        return header;
    }

    /// <summary>
    /// Writes the header to the given byte span.
    /// </summary>
    /// <param name="data">The destination span (must be at least 18 bytes).</param>
    public void WriteTo(Span<byte> data)
    {
        if (data.Length < Size)
            throw new ArgumentException($"Destination must be at least {Size} bytes.", nameof(data));

        data[0] = IdLength;
        data[1] = ColorMapType;
        data[2] = (byte)ImageType;
        BinaryPrimitives.WriteUInt16LittleEndian(data.Slice(3), ColorMapOrigin);
        BinaryPrimitives.WriteUInt16LittleEndian(data.Slice(5), ColorMapLength);
        data[7] = ColorMapDepth;
        BinaryPrimitives.WriteUInt16LittleEndian(data.Slice(8), XOrigin);
        BinaryPrimitives.WriteUInt16LittleEndian(data.Slice(10), YOrigin);
        BinaryPrimitives.WriteUInt16LittleEndian(data.Slice(12), Width);
        BinaryPrimitives.WriteUInt16LittleEndian(data.Slice(14), Height);
        data[16] = PixelDepth;
        data[17] = ImageDescriptor;
    }

    /// <summary>
    /// Creates a header for encoding with the specified parameters.
    /// </summary>
    public static TgaHeader CreateForEncoding(int width, int height, TgaBitsPerPixel bitsPerPixel, bool useRle)
    {
        if (width > ushort.MaxValue || height > ushort.MaxValue)
            throw new ArgumentException("Image dimensions exceed TGA maximum (65535).");

        byte alphaBits = bitsPerPixel == TgaBitsPerPixel.Bit32 ? (byte)8 : (byte)0;

        return new TgaHeader
        {
            IdLength = 0,
            ColorMapType = 0,
            ImageType = useRle ? TgaImageType.RleTrueColor : TgaImageType.UncompressedTrueColor,
            ColorMapOrigin = 0,
            ColorMapLength = 0,
            ColorMapDepth = 0,
            XOrigin = 0,
            YOrigin = 0,
            Width = (ushort)width,
            Height = (ushort)height,
            PixelDepth = (byte)bitsPerPixel,
            // Top-left origin (bit 5 = 1) + alpha bits
            ImageDescriptor = (byte)((2 << 4) | alphaBits)
        };
    }

    private static bool IsValidImageType(TgaImageType type)
    {
        return type == TgaImageType.NoData ||
               type == TgaImageType.UncompressedColorMapped ||
               type == TgaImageType.UncompressedTrueColor ||
               type == TgaImageType.UncompressedGrayscale ||
               type == TgaImageType.RleColorMapped ||
               type == TgaImageType.RleTrueColor ||
               type == TgaImageType.RleGrayscale;
    }
}
