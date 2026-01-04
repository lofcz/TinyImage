namespace TinyImage.Codecs.Pnm;

/// <summary>
/// Represents the header information of a Netpbm (PBM/PGM/PPM) image file.
/// </summary>
internal readonly struct PnmHeader
{
    /// <summary>
    /// Gets the format variant (P1-P6).
    /// </summary>
    public PnmFormat Format { get; }

    /// <summary>
    /// Gets the image width in pixels.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets the image height in pixels.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Gets the maximum pixel value.
    /// For PBM (P1/P4), this is always 1.
    /// For PGM/PPM (P2/P3/P5/P6), this ranges from 1 to 65535.
    /// </summary>
    public int MaxValue { get; }

    /// <summary>
    /// Gets the byte offset where pixel data begins in the stream.
    /// </summary>
    public int PixelDataOffset { get; }

    /// <summary>
    /// Gets the optional comment from the header.
    /// </summary>
    public string? Comment { get; }

    /// <summary>
    /// Gets whether this format uses 16-bit samples (maxval > 255).
    /// </summary>
    public bool Is16Bit => MaxValue > 255;

    /// <summary>
    /// Gets the number of bytes per sample in binary formats.
    /// </summary>
    public int BytesPerSample => Is16Bit ? 2 : 1;

    /// <summary>
    /// Gets the number of color channels.
    /// </summary>
    public int ChannelCount => Format.GetChannelCount();

    /// <summary>
    /// Creates a new PnmHeader.
    /// </summary>
    /// <param name="format">The format variant.</param>
    /// <param name="width">The image width.</param>
    /// <param name="height">The image height.</param>
    /// <param name="maxValue">The maximum pixel value.</param>
    /// <param name="pixelDataOffset">The byte offset to pixel data.</param>
    /// <param name="comment">Optional comment from the header.</param>
    public PnmHeader(PnmFormat format, int width, int height, int maxValue, int pixelDataOffset, string? comment = null)
    {
        Format = format;
        Width = width;
        Height = height;
        MaxValue = maxValue;
        PixelDataOffset = pixelDataOffset;
        Comment = comment;
    }

    /// <summary>
    /// Calculates the expected size of pixel data in bytes for binary formats.
    /// </summary>
    public int CalculateBinaryDataSize()
    {
        if (Format.IsBitmap())
        {
            // P4: bits packed into bytes, rows padded to byte boundary
            int bytesPerRow = (Width + 7) / 8;
            return bytesPerRow * Height;
        }
        else
        {
            // P5/P6: samples stored as bytes (1 or 2 bytes per sample)
            return Width * Height * ChannelCount * BytesPerSample;
        }
    }

    /// <summary>
    /// Validates the header values.
    /// </summary>
    /// <returns>True if the header is valid, false otherwise.</returns>
    public bool IsValid()
    {
        if (Width <= 0 || Height <= 0)
            return false;

        if (MaxValue < 1 || MaxValue > 65535)
            return false;

        if (Format.IsBitmap() && MaxValue != 1)
            return false;

        if (PixelDataOffset < 0)
            return false;

        return true;
    }
}
