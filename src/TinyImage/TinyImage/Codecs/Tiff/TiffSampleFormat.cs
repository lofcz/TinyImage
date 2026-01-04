namespace TinyImage.Codecs.Tiff;

/// <summary>
/// TIFF sample format values.
/// Based on image-tiff tags.rs lines 304-314.
/// </summary>
internal enum TiffSampleFormat : ushort
{
    /// <summary>
    /// Unsigned integer data.
    /// </summary>
    UnsignedInt = 1,

    /// <summary>
    /// Signed integer data.
    /// </summary>
    SignedInt = 2,

    /// <summary>
    /// IEEE floating point data.
    /// </summary>
    Float = 3,

    /// <summary>
    /// Undefined data format.
    /// </summary>
    Undefined = 4
}
