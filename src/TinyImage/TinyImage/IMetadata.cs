namespace TinyImage;

/// <summary>
/// Interface for metadata types that can be cloned (deep copied).
/// </summary>
internal interface ICloneableMetadata
{
    /// <summary>
    /// Creates a deep copy of this metadata.
    /// </summary>
    object Clone();
}

/// <summary>
/// Interface for metadata types that need to transform when an image is resized.
/// </summary>
/// <remarks>
/// Implement this interface on format-specific metadata classes that contain
/// coordinate-based data (like cursor hotspots) that should scale with the image.
/// </remarks>
internal interface IMetadata
{
    /// <summary>
    /// Called when the image is resized to allow the metadata to scale coordinate-based values.
    /// </summary>
    /// <param name="oldWidth">Original image width.</param>
    /// <param name="oldHeight">Original image height.</param>
    /// <param name="newWidth">New image width.</param>
    /// <param name="newHeight">New image height.</param>
    void OnImageResized(int oldWidth, int oldHeight, int newWidth, int newHeight);
}
