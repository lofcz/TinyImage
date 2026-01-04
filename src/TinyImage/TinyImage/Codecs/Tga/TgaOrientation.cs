namespace TinyImage.Codecs.Tga;

/// <summary>
/// Specifies the screen origin (pixel ordering) of a TGA image.
/// </summary>
internal enum TgaOrientation : byte
{
    /// <summary>
    /// First pixel corresponds to the bottom-left corner.
    /// This is the default TGA orientation.
    /// </summary>
    BottomLeft = 0,

    /// <summary>
    /// First pixel corresponds to the bottom-right corner.
    /// </summary>
    BottomRight = 1,

    /// <summary>
    /// First pixel corresponds to the top-left corner.
    /// </summary>
    TopLeft = 2,

    /// <summary>
    /// First pixel corresponds to the top-right corner.
    /// </summary>
    TopRight = 3
}
