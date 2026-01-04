namespace TinyImage.Codecs.Tiff;

/// <summary>
/// TIFF extra samples interpretation values.
/// Based on image-tiff tags.rs lines 316-325.
/// </summary>
internal enum TiffExtraSamples : ushort
{
    /// <summary>
    /// Unspecified data.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// Associated alpha data (pre-multiplied).
    /// </summary>
    AssociatedAlpha = 1,

    /// <summary>
    /// Unassociated alpha data (not pre-multiplied).
    /// </summary>
    UnassociatedAlpha = 2
}
