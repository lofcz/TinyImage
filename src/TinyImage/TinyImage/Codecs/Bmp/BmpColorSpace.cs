namespace TinyImage.Codecs.Bmp;

/// <summary>
/// Defines the color space type for BMP V4 and V5 headers.
/// </summary>
internal enum BmpColorSpace : uint
{
    /// <summary>
    /// Endpoints and gamma values are given in the appropriate fields.
    /// </summary>
    LCS_CALIBRATED_RGB = 0,

    /// <summary>
    /// The Windows default color space ('Win ').
    /// </summary>
    LCS_WINDOWS_COLOR_SPACE = 0x57696E20,

    /// <summary>
    /// Specifies that the bitmap is in sRGB color space ('sRGB').
    /// </summary>
    LCS_sRGB = 0x73524742,

    /// <summary>
    /// This value indicates that bV5ProfileData points to the file name
    /// of the profile to use (gamma and endpoints values are ignored).
    /// </summary>
    PROFILE_LINKED = 0x4C494E4B,

    /// <summary>
    /// This value indicates that bV5ProfileData points to a memory buffer
    /// that contains the profile to be used (gamma and endpoints values are ignored).
    /// </summary>
    PROFILE_EMBEDDED = 0x4D424544
}
