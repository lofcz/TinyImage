namespace TinyImage.Codecs.Tga;

/// <summary>
/// Specifies the bit depth for TGA encoding.
/// </summary>
public enum TgaBitsPerPixel : byte
{
    /// <summary>
    /// 24 bits per pixel (BGR, no alpha).
    /// </summary>
    Bit24 = 24,

    /// <summary>
    /// 32 bits per pixel (BGRA, with alpha).
    /// </summary>
    Bit32 = 32
}
