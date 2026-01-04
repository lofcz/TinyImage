using System;

namespace TinyImage.Codecs.Ico;

/// <summary>
/// Represents one entry in an ICO or CUR file (a single icon or cursor image).
/// </summary>
internal sealed class IcoDirectoryEntry
{
    /// <summary>
    /// PNG file signature.
    /// </summary>
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47 };

    /// <summary>
    /// Gets or sets the type of resource (Icon or Cursor).
    /// </summary>
    public IcoResourceType ResourceType { get; set; }

    /// <summary>
    /// Gets or sets the width of the image in pixels.
    /// </summary>
    public uint Width { get; set; }

    /// <summary>
    /// Gets or sets the height of the image in pixels.
    /// </summary>
    public uint Height { get; set; }

    /// <summary>
    /// Gets or sets the number of colors (0 if >= 256 colors).
    /// </summary>
    public byte NumColors { get; set; }

    /// <summary>
    /// Gets or sets the color planes (for icons) or X hotspot (for cursors).
    /// </summary>
    public ushort ColorPlanesOrHotspotX { get; set; }

    /// <summary>
    /// Gets or sets the bits per pixel (for icons) or Y hotspot (for cursors).
    /// </summary>
    public ushort BitsPerPixelOrHotspotY { get; set; }

    /// <summary>
    /// Gets or sets the raw encoded image data (BMP or PNG).
    /// </summary>
    public byte[] Data { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Gets the bits per pixel if this is an icon entry.
    /// Returns 0 if this is a cursor entry.
    /// </summary>
    public ushort BitsPerPixel => ResourceType == IcoResourceType.Cursor ? (ushort)0 : BitsPerPixelOrHotspotY;

    /// <summary>
    /// Gets the cursor hotspot coordinates, or null if this is an icon.
    /// </summary>
    public (ushort X, ushort Y)? CursorHotspot =>
        ResourceType == IcoResourceType.Cursor
            ? (ColorPlanesOrHotspotX, BitsPerPixelOrHotspotY)
            : null;

    /// <summary>
    /// Gets whether the image data is PNG encoded.
    /// </summary>
    public bool IsPng
    {
        get
        {
            if (Data.Length < 4)
                return false;
            return Data[0] == PngSignature[0] &&
                   Data[1] == PngSignature[1] &&
                   Data[2] == PngSignature[2] &&
                   Data[3] == PngSignature[3];
        }
    }
}
