namespace TinyImage.Codecs.Ico;

/// <summary>
/// BMP color depth options for ICO encoding.
/// </summary>
internal enum IcoBmpDepth
{
    /// <summary>
    /// 1 bit per pixel (2 colors).
    /// </summary>
    One = 1,

    /// <summary>
    /// 4 bits per pixel (16 colors).
    /// </summary>
    Four = 4,

    /// <summary>
    /// 8 bits per pixel (256 colors).
    /// </summary>
    Eight = 8,

    /// <summary>
    /// 16 bits per pixel (RGB555).
    /// </summary>
    Sixteen = 16,

    /// <summary>
    /// 24 bits per pixel (RGB).
    /// </summary>
    TwentyFour = 24,

    /// <summary>
    /// 32 bits per pixel (RGBA).
    /// </summary>
    ThirtyTwo = 32
}

/// <summary>
/// Extension methods for IcoBmpDepth.
/// </summary>
internal static class IcoBmpDepthExtensions
{
    /// <summary>
    /// Converts bits per pixel value to IcoBmpDepth.
    /// </summary>
    public static IcoBmpDepth? FromBitsPerPixel(ushort bitsPerPixel)
    {
        return bitsPerPixel switch
        {
            1 => IcoBmpDepth.One,
            4 => IcoBmpDepth.Four,
            8 => IcoBmpDepth.Eight,
            16 => IcoBmpDepth.Sixteen,
            24 => IcoBmpDepth.TwentyFour,
            32 => IcoBmpDepth.ThirtyTwo,
            _ => null
        };
    }

    /// <summary>
    /// Gets the number of colors in the color table for this depth.
    /// </summary>
    public static int GetNumColors(this IcoBmpDepth depth)
    {
        return depth switch
        {
            IcoBmpDepth.One => 2,
            IcoBmpDepth.Four => 16,
            IcoBmpDepth.Eight => 256,
            _ => 0
        };
    }
}
