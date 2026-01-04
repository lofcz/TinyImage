using System;
using System.Buffers.Binary;

namespace TinyImage.Codecs.Bmp;

/// <summary>
/// This block of bytes tells the application detailed information
/// about the image, which will be used to display the image on the screen.
/// Supports all BMP header versions (V2 through V5).
/// </summary>
internal struct BmpInfoHeader
{
    /// <summary>
    /// Gets or sets the size of this header in bytes.
    /// </summary>
    public int HeaderSize { get; set; }

    /// <summary>
    /// Gets or sets the bitmap width in pixels.
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// Gets or sets the bitmap height in pixels.
    /// Positive values indicate bottom-up orientation.
    /// Negative values indicate top-down orientation.
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// Gets or sets the number of color planes. Must be 1.
    /// </summary>
    public short Planes { get; set; }

    /// <summary>
    /// Gets or sets the number of bits per pixel (color depth).
    /// Typical values are 1, 2, 4, 8, 16, 24, and 32.
    /// </summary>
    public ushort BitsPerPixel { get; set; }

    /// <summary>
    /// Gets or sets the compression method being used.
    /// </summary>
    public BmpCompression Compression { get; set; }

    /// <summary>
    /// Gets or sets the raw image data size in bytes.
    /// May be 0 for uncompressed RGB bitmaps.
    /// </summary>
    public int ImageSize { get; set; }

    /// <summary>
    /// Gets or sets the horizontal resolution (pixels per meter).
    /// </summary>
    public int XPelsPerMeter { get; set; }

    /// <summary>
    /// Gets or sets the vertical resolution (pixels per meter).
    /// </summary>
    public int YPelsPerMeter { get; set; }

    /// <summary>
    /// Gets or sets the number of colors in the color palette,
    /// or 0 to default to 2^n.
    /// </summary>
    public int ColorsUsed { get; set; }

    /// <summary>
    /// Gets or sets the number of important colors used,
    /// or 0 when every color is important.
    /// </summary>
    public int ColorsImportant { get; set; }

    /// <summary>
    /// Gets or sets the red color mask for BitFields compression.
    /// </summary>
    public uint RedMask { get; set; }

    /// <summary>
    /// Gets or sets the green color mask for BitFields compression.
    /// </summary>
    public uint GreenMask { get; set; }

    /// <summary>
    /// Gets or sets the blue color mask for BitFields compression.
    /// </summary>
    public uint BlueMask { get; set; }

    /// <summary>
    /// Gets or sets the alpha color mask for BitFields compression.
    /// </summary>
    public uint AlphaMask { get; set; }

    /// <summary>
    /// Gets or sets the color space type (V4/V5 headers).
    /// </summary>
    public BmpColorSpace ColorSpaceType { get; set; }
    
    /// <summary>
    /// Gets or sets the rendering intent (V5 headers only).
    /// </summary>
    public BmpRenderingIntent RenderingIntent { get; set; }
    
    /// <summary>
    /// Gets or sets the offset to ICC profile data from start of header (V5 headers only).
    /// </summary>
    public int ProfileDataOffset { get; set; }
    
    /// <summary>
    /// Gets or sets the size of embedded ICC profile in bytes (V5 headers only).
    /// </summary>
    public int ProfileSize { get; set; }

    /// <summary>
    /// Gets whether this is a bottom-up bitmap (positive height).
    /// </summary>
    public bool IsBottomUp => Height >= 0;

    /// <summary>
    /// Gets the absolute height of the image.
    /// </summary>
    public int AbsoluteHeight => Math.Abs(Height);

    /// <summary>
    /// Parses the header size from the first 4 bytes to determine header type.
    /// </summary>
    public static int ReadHeaderSize(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
            throw new ArgumentException("Data must be at least 4 bytes.", nameof(data));
        return BinaryPrimitives.ReadInt32LittleEndian(data);
    }

    /// <summary>
    /// Parses a BMP info header from the given data, auto-detecting version.
    /// </summary>
    /// <param name="data">The raw header bytes.</param>
    /// <returns>The parsed info header.</returns>
    public static BmpInfoHeader Parse(ReadOnlySpan<byte> data)
    {
        int headerSize = ReadHeaderSize(data);

        return headerSize switch
        {
            BmpConstants.HeaderSizes.CoreHeader => ParseCoreHeader(data),
            BmpConstants.HeaderSizes.Os22ShortHeader => ParseOs22ShortHeader(data),
            BmpConstants.HeaderSizes.InfoHeaderV3 => ParseV3Header(data),
            BmpConstants.HeaderSizes.AdobeV3Header => ParseAdobeV3Header(data, withAlpha: false),
            BmpConstants.HeaderSizes.AdobeV3WithAlphaHeader => ParseAdobeV3Header(data, withAlpha: true),
            BmpConstants.HeaderSizes.Os2V2Header => ParseOs2V2Header(data),
            BmpConstants.HeaderSizes.InfoHeaderV4 => ParseV4Header(data),
            BmpConstants.HeaderSizes.InfoHeaderV5 => ParseV5Header(data),
            _ when headerSize > BmpConstants.HeaderSizes.InfoHeaderV3 => ParseV3Header(data),
            _ => throw new NotSupportedException($"Unsupported BMP header size: {headerSize}")
        };
    }

    /// <summary>
    /// Parses the BITMAPCOREHEADER (12 bytes, BMP Version 2).
    /// </summary>
    private static BmpInfoHeader ParseCoreHeader(ReadOnlySpan<byte> data)
    {
        return new BmpInfoHeader
        {
            HeaderSize = BinaryPrimitives.ReadInt32LittleEndian(data),
            Width = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(4)),
            Height = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(6)),
            Planes = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(8)),
            BitsPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(10)),
            Compression = BmpCompression.RGB
        };
    }

    /// <summary>
    /// Parses the short OS/2 2.x header (16 bytes).
    /// </summary>
    private static BmpInfoHeader ParseOs22ShortHeader(ReadOnlySpan<byte> data)
    {
        return new BmpInfoHeader
        {
            HeaderSize = BinaryPrimitives.ReadInt32LittleEndian(data),
            Width = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(4)),
            Height = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(8)),
            Planes = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(12)),
            BitsPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(14)),
            Compression = BmpCompression.RGB
        };
    }

    /// <summary>
    /// Parses the BITMAPINFOHEADER (40 bytes, BMP Version 3).
    /// </summary>
    private static BmpInfoHeader ParseV3Header(ReadOnlySpan<byte> data)
    {
        var header = new BmpInfoHeader
        {
            HeaderSize = BinaryPrimitives.ReadInt32LittleEndian(data),
            Width = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(4)),
            Height = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(8)),
            Planes = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(12)),
            BitsPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(14)),
            Compression = (BmpCompression)BinaryPrimitives.ReadInt32LittleEndian(data.Slice(16)),
            ImageSize = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(20)),
            XPelsPerMeter = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(24)),
            YPelsPerMeter = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(28)),
            ColorsUsed = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(32)),
            ColorsImportant = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(36))
        };

        // Set default masks for 16-bit and 32-bit images
        SetDefaultMasks(ref header);

        return header;
    }

    /// <summary>
    /// Parses the Adobe V3 header variant (52 or 56 bytes).
    /// </summary>
    private static BmpInfoHeader ParseAdobeV3Header(ReadOnlySpan<byte> data, bool withAlpha)
    {
        var header = ParseV3Header(data);
        header.RedMask = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(40));
        header.GreenMask = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(44));
        header.BlueMask = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(48));
        if (withAlpha)
        {
            header.AlphaMask = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(52));
        }
        return header;
    }

    /// <summary>
    /// Parses the OS/2 2.x header (64 bytes).
    /// </summary>
    private static BmpInfoHeader ParseOs2V2Header(ReadOnlySpan<byte> data)
    {
        var header = new BmpInfoHeader
        {
            HeaderSize = BinaryPrimitives.ReadInt32LittleEndian(data),
            Width = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(4)),
            Height = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(8)),
            Planes = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(12)),
            BitsPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(14)),
            ImageSize = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(20)),
            XPelsPerMeter = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(24)),
            YPelsPerMeter = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(28)),
            ColorsUsed = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(32)),
            ColorsImportant = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(36))
        };

        // Map OS/2 compression values to Windows values
        int os2Compression = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(16));
        header.Compression = os2Compression switch
        {
            0 => BmpCompression.RGB,
            1 => BmpCompression.RLE8,
            2 => BmpCompression.RLE4,
            4 => BmpCompression.RLE24,
            _ => throw new NotSupportedException($"Unsupported OS/2 compression type: {os2Compression}")
        };

        SetDefaultMasks(ref header);
        return header;
    }

    /// <summary>
    /// Parses the BITMAPV4HEADER (108 bytes).
    /// </summary>
    private static BmpInfoHeader ParseV4Header(ReadOnlySpan<byte> data)
    {
        var header = ParseV3Header(data);
        header.RedMask = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(40));
        header.GreenMask = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(44));
        header.BlueMask = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(48));
        header.AlphaMask = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(52));
        header.ColorSpaceType = (BmpColorSpace)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(56));
        // Endpoint and gamma values (60-107) are parsed but not used for decoding
        return header;
    }

    /// <summary>
    /// Parses the BITMAPV5HEADER (124 bytes).
    /// </summary>
    private static BmpInfoHeader ParseV5Header(ReadOnlySpan<byte> data)
    {
        // V5 extends V4 with intent, profile data, and reserved fields
        var header = ParseV4Header(data);
        
        // V5-specific fields at offsets 108-123
        if (data.Length >= BmpConstants.HeaderSizes.InfoHeaderV5)
        {
            header.RenderingIntent = (BmpRenderingIntent)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(108));
            header.ProfileDataOffset = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(112));
            header.ProfileSize = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(116));
            // Bytes 120-123 are reserved
        }
        
        return header;
    }

    /// <summary>
    /// Sets default color masks based on bits per pixel.
    /// </summary>
    private static void SetDefaultMasks(ref BmpInfoHeader header)
    {
        if (header.BitsPerPixel == 64)
        {
            // 64-bit: 16-bit per channel in BGRA order (s2.13 fixed-point)
            // Masks aren't really used for 64-bit since format is fixed
            // Just use placeholder values (actual decoding uses fixed format)
            header.RedMask = 0x00FF0000;
            header.GreenMask = 0x0000FF00;
            header.BlueMask = 0x000000FF;
            header.AlphaMask = 0xFF000000;
        }
        else if (header.BitsPerPixel == 32)
        {
            if (header.RedMask == 0 && header.GreenMask == 0 && header.BlueMask == 0)
            {
                header.RedMask = 0x00FF0000;
                header.GreenMask = 0x0000FF00;
                header.BlueMask = 0x000000FF;
                header.AlphaMask = 0xFF000000;
            }
        }
        else if (header.BitsPerPixel == 16)
        {
            if (header.RedMask == 0 && header.GreenMask == 0 && header.BlueMask == 0)
            {
                // Default 5-5-5 format
                header.RedMask = 0x7C00;
                header.GreenMask = 0x03E0;
                header.BlueMask = 0x001F;
                header.AlphaMask = 0;
            }
        }
    }

    /// <summary>
    /// Writes a V3 info header (40 bytes) to the given buffer.
    /// </summary>
    public void WriteV3Header(Span<byte> buffer)
    {
        if (buffer.Length < BmpConstants.HeaderSizes.InfoHeaderV3)
            throw new ArgumentException($"Buffer must be at least {BmpConstants.HeaderSizes.InfoHeaderV3} bytes.");

        buffer.Clear();
        BinaryPrimitives.WriteInt32LittleEndian(buffer, BmpConstants.HeaderSizes.InfoHeaderV3);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(4), Width);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(8), Height);
        BinaryPrimitives.WriteInt16LittleEndian(buffer.Slice(12), Planes);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(14), BitsPerPixel);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(16), (int)Compression);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(20), ImageSize);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(24), XPelsPerMeter);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(28), YPelsPerMeter);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(32), ColorsUsed);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(36), ColorsImportant);
    }

    /// <summary>
    /// Writes a V4 info header (108 bytes) to the given buffer.
    /// </summary>
    public void WriteV4Header(Span<byte> buffer)
    {
        if (buffer.Length < BmpConstants.HeaderSizes.InfoHeaderV4)
            throw new ArgumentException($"Buffer must be at least {BmpConstants.HeaderSizes.InfoHeaderV4} bytes.");

        buffer.Clear();
        BinaryPrimitives.WriteInt32LittleEndian(buffer, BmpConstants.HeaderSizes.InfoHeaderV4);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(4), Width);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(8), Height);
        BinaryPrimitives.WriteInt16LittleEndian(buffer.Slice(12), Planes);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(14), BitsPerPixel);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(16), (int)Compression);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(20), ImageSize);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(24), XPelsPerMeter);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(28), YPelsPerMeter);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(32), ColorsUsed);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(36), ColorsImportant);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(40), RedMask);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(44), GreenMask);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(48), BlueMask);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(52), AlphaMask);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(56), (uint)ColorSpaceType);
        // Remaining bytes (endpoints, gamma) left as zeros
    }
    
    /// <summary>
    /// Writes a V5 info header (124 bytes) to the given buffer.
    /// Supports ICC profile embedding and rendering intent.
    /// </summary>
    public void WriteV5Header(Span<byte> buffer)
    {
        if (buffer.Length < BmpConstants.HeaderSizes.InfoHeaderV5)
            throw new ArgumentException($"Buffer must be at least {BmpConstants.HeaderSizes.InfoHeaderV5} bytes.");

        buffer.Clear();
        BinaryPrimitives.WriteInt32LittleEndian(buffer, BmpConstants.HeaderSizes.InfoHeaderV5);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(4), Width);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(8), Height);
        BinaryPrimitives.WriteInt16LittleEndian(buffer.Slice(12), Planes);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(14), BitsPerPixel);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(16), (int)Compression);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(20), ImageSize);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(24), XPelsPerMeter);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(28), YPelsPerMeter);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(32), ColorsUsed);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(36), ColorsImportant);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(40), RedMask);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(44), GreenMask);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(48), BlueMask);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(52), AlphaMask);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(56), (uint)ColorSpaceType);
        // Endpoints and gamma (bytes 60-107) left as zeros for sRGB
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(108), (uint)RenderingIntent);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(112), ProfileDataOffset);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(116), ProfileSize);
        // Reserved (bytes 120-123) left as zeros
    }

    /// <summary>
    /// Gets the number of color palette entries.
    /// </summary>
    public int GetPaletteColorCount()
    {
        if (BitsPerPixel > 8)
            return 0;

        if (ColorsUsed > 0)
            return ColorsUsed;

        return 1 << BitsPerPixel;
    }

    /// <summary>
    /// Gets the bytes per palette entry (3 for core header, 4 for others).
    /// </summary>
    public int GetPaletteEntrySize()
    {
        return HeaderSize == BmpConstants.HeaderSizes.CoreHeader ? 3 : 4;
    }
}
