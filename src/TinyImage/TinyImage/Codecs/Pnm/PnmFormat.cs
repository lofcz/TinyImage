namespace TinyImage.Codecs.Pnm;

/// <summary>
/// Specifies the Netpbm format variant.
/// </summary>
public enum PnmFormat
{
    /// <summary>
    /// PBM ASCII format - Portable Bitmap (black and white).
    /// Pixels are represented as ASCII '0' (white) and '1' (black).
    /// </summary>
    P1 = 1,

    /// <summary>
    /// PGM ASCII format - Portable Graymap (grayscale).
    /// Pixels are represented as ASCII decimal values from 0 to maxval.
    /// </summary>
    P2 = 2,

    /// <summary>
    /// PPM ASCII format - Portable Pixmap (RGB color).
    /// Pixels are represented as ASCII decimal RGB triplets from 0 to maxval.
    /// </summary>
    P3 = 3,

    /// <summary>
    /// PBM Binary format - Portable Bitmap (black and white).
    /// Pixels are packed as bits, 8 pixels per byte, MSB first.
    /// </summary>
    P4 = 4,

    /// <summary>
    /// PGM Binary format - Portable Graymap (grayscale).
    /// Pixels are stored as 1 or 2 bytes per sample (big-endian for 16-bit).
    /// </summary>
    P5 = 5,

    /// <summary>
    /// PPM Binary format - Portable Pixmap (RGB color).
    /// Pixels are stored as 3 or 6 bytes per pixel (big-endian for 16-bit).
    /// </summary>
    P6 = 6
}

/// <summary>
/// Extension methods for PnmFormat.
/// </summary>
internal static class PnmFormatExtensions
{
    /// <summary>
    /// Gets whether this format uses ASCII (plain text) encoding.
    /// </summary>
    public static bool IsAscii(this PnmFormat format) =>
        format is PnmFormat.P1 or PnmFormat.P2 or PnmFormat.P3;

    /// <summary>
    /// Gets whether this format uses binary (raw) encoding.
    /// </summary>
    public static bool IsBinary(this PnmFormat format) =>
        format is PnmFormat.P4 or PnmFormat.P5 or PnmFormat.P6;

    /// <summary>
    /// Gets whether this format is a bitmap (black and white only).
    /// </summary>
    public static bool IsBitmap(this PnmFormat format) =>
        format is PnmFormat.P1 or PnmFormat.P4;

    /// <summary>
    /// Gets whether this format is a graymap (grayscale).
    /// </summary>
    public static bool IsGraymap(this PnmFormat format) =>
        format is PnmFormat.P2 or PnmFormat.P5;

    /// <summary>
    /// Gets whether this format is a pixmap (RGB color).
    /// </summary>
    public static bool IsPixmap(this PnmFormat format) =>
        format is PnmFormat.P3 or PnmFormat.P6;

    /// <summary>
    /// Gets the number of color channels for this format.
    /// </summary>
    public static int GetChannelCount(this PnmFormat format) =>
        format.IsPixmap() ? 3 : 1;

    /// <summary>
    /// Gets the magic number string for this format.
    /// </summary>
    public static string GetMagicNumber(this PnmFormat format) =>
        $"P{(int)format}";

    /// <summary>
    /// Parses a magic number string to a PnmFormat.
    /// </summary>
    /// <param name="magic">The magic number string (e.g., "P6").</param>
    /// <param name="format">The parsed format.</param>
    /// <returns>True if parsing succeeded, false otherwise.</returns>
    public static bool TryParse(string magic, out PnmFormat format)
    {
        format = default;
        if (magic == null || magic.Length < 2 || magic[0] != 'P')
            return false;

        if (magic[1] >= '1' && magic[1] <= '6')
        {
            format = (PnmFormat)(magic[1] - '0');
            return true;
        }

        return false;
    }
}
