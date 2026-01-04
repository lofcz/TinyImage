namespace TinyImage.Codecs.Png;

/// <summary>
/// Indicates the transmission order of the image data.
/// </summary>
internal enum PngInterlaceMethod : byte
{
    /// <summary>
    /// No interlace.
    /// </summary>
    None = 0,
    /// <summary>
    /// Adam7 interlace.
    /// </summary>
    Adam7 = 1
}
