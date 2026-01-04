using System.Collections.Generic;

namespace TinyImage.Codecs.Ico;

/// <summary>
/// Statistics about an image used to determine optimal encoding.
/// </summary>
internal sealed class IcoImageStats
{
    /// <summary>
    /// True if the image uses transparency (any alpha less than 255).
    /// </summary>
    public bool HasAlpha { get; set; }

    /// <summary>
    /// True if the image has alpha values between 0 and 255 exclusive.
    /// BMP format can only support non-binary alpha at 32bpp.
    /// </summary>
    public bool HasNonBinaryAlpha { get; set; }

    /// <summary>
    /// Set of unique RGB colors in the image (max 257 tracked).
    /// Null if more than 256 colors.
    /// </summary>
    public HashSet<(byte R, byte G, byte B)>? Colors { get; set; }

    /// <summary>
    /// Computes image statistics from RGBA pixel data.
    /// </summary>
    public static IcoImageStats Compute(byte[] rgba)
    {
        var stats = new IcoImageStats
        {
            Colors = new HashSet<(byte R, byte G, byte B)>()
        };

        for (int i = 0; i < rgba.Length; i += 4)
        {
            byte r = rgba[i];
            byte g = rgba[i + 1];
            byte b = rgba[i + 2];
            byte a = rgba[i + 3];

            if (a != 255)
            {
                stats.HasAlpha = true;
                if (a != 0)
                {
                    stats.HasNonBinaryAlpha = true;
                }
            }

            // Only track up to 256 colors
            if (stats.Colors != null && stats.Colors.Count <= 256)
            {
                stats.Colors.Add((r, g, b));
            }
            else if (stats.Colors != null && stats.Colors.Count > 256)
            {
                stats.Colors = null; // Too many colors
            }
        }

        return stats;
    }
}
