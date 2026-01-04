namespace TinyImage.Codecs.Bmp;

/// <summary>
/// Enumerates the available bits per pixel the bitmap codec supports.
/// </summary>
public enum BmpBitsPerPixel : short
{
    /// <summary>
    /// 1 bit per pixel. Monochrome, 2 colors.
    /// </summary>
    Bit1 = 1,

    /// <summary>
    /// 2 bits per pixel. 4 colors.
    /// </summary>
    Bit2 = 2,

    /// <summary>
    /// 4 bits per pixel. 16 colors.
    /// </summary>
    Bit4 = 4,

    /// <summary>
    /// 8 bits per pixel. 256 colors. Each pixel consists of 1 byte.
    /// </summary>
    Bit8 = 8,

    /// <summary>
    /// 16 bits per pixel. Each pixel consists of 2 bytes.
    /// </summary>
    Bit16 = 16,

    /// <summary>
    /// 24 bits per pixel. Each pixel consists of 3 bytes (BGR).
    /// </summary>
    Bit24 = 24,

    /// <summary>
    /// 32 bits per pixel. Each pixel consists of 4 bytes (BGRA).
    /// </summary>
    Bit32 = 32,

    /// <summary>
    /// 64 bits per pixel. Each pixel consists of 8 bytes (16-bit RGBA).
    /// Data is stored as s2.13 fixed-point in linear light.
    /// Supported by GIMP 3.0+ and some Microsoft tools.
    /// </summary>
    Bit64 = 64
}
