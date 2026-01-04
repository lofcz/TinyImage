namespace TinyImage.Codecs.WebP;

/// <summary>
/// Contains metadata from VP8X extended format header.
/// </summary>
internal class WebPExtendedInfo
{
    /// <summary>Whether the image has alpha channel</summary>
    public bool HasAlpha { get; set; }

    /// <summary>Canvas width in pixels</summary>
    public int CanvasWidth { get; set; }

    /// <summary>Canvas height in pixels</summary>
    public int CanvasHeight { get; set; }

    /// <summary>Whether the image has an ICC color profile</summary>
    public bool HasIccProfile { get; set; }

    /// <summary>Whether the image has EXIF metadata</summary>
    public bool HasExifMetadata { get; set; }

    /// <summary>Whether the image has XMP metadata</summary>
    public bool HasXmpMetadata { get; set; }

    /// <summary>Whether the image is animated</summary>
    public bool IsAnimated { get; set; }

    /// <summary>Background color hint for animations (BGRA)</summary>
    public byte[] BackgroundColorHint { get; set; } = new byte[4];

    /// <summary>Background color to use for compositing (BGRA), null if not set</summary>
    public byte[] BackgroundColor { get; set; }
}

/// <summary>
/// Animation frame information from ANMF chunk.
/// </summary>
internal class WebPAnimationFrame
{
    /// <summary>X offset of frame from left of canvas</summary>
    public int OffsetX { get; set; }

    /// <summary>Y offset of frame from top of canvas</summary>
    public int OffsetY { get; set; }

    /// <summary>Frame width in pixels</summary>
    public int Width { get; set; }

    /// <summary>Frame height in pixels</summary>
    public int Height { get; set; }

    /// <summary>Frame duration in milliseconds</summary>
    public int Duration { get; set; }

    /// <summary>Whether to use alpha blending (true) or replace (false)</summary>
    public bool UseAlphaBlending { get; set; }

    /// <summary>Whether to dispose frame to background after display</summary>
    public bool DisposeToBackground { get; set; }
}

/// <summary>
/// Alpha channel filtering methods.
/// </summary>
internal enum AlphaFilteringMethod
{
    None = 0,
    Horizontal = 1,
    Vertical = 2,
    Gradient = 3
}

/// <summary>
/// Alpha chunk information.
/// </summary>
internal class AlphaChunk
{
    /// <summary>Whether preprocessing was applied</summary>
    public bool Preprocessing { get; set; }

    /// <summary>Filtering method used</summary>
    public AlphaFilteringMethod FilteringMethod { get; set; }

    /// <summary>Decoded alpha data</summary>
    public byte[] Data { get; set; }
}
