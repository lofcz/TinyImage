using System;
using System.Runtime.CompilerServices;

namespace TinyImage;

/// <summary>
/// Represents a single frame within an image.
/// For single-frame formats (PNG, JPEG), the image contains one frame.
/// For animated formats (GIF, WebP, APNG), the image may contain multiple frames.
/// </summary>
public sealed class ImageFrame
{
    private readonly PixelBuffer _buffer;

    /// <summary>
    /// Gets the width of the frame in pixels.
    /// </summary>
    public int Width => _buffer.Width;

    /// <summary>
    /// Gets the height of the frame in pixels.
    /// </summary>
    public int Height => _buffer.Height;

    /// <summary>
    /// Gets or sets the duration this frame should be displayed.
    /// Used for animated formats like GIF, WebP, and APNG.
    /// For static images, this is typically <see cref="TimeSpan.Zero"/>.
    /// </summary>
    public TimeSpan Duration { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Creates a new frame with the specified dimensions.
    /// </summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    public ImageFrame(int width, int height)
    {
        _buffer = new PixelBuffer(width, height);
    }

    /// <summary>
    /// Creates a new frame from an existing pixel buffer.
    /// </summary>
    /// <param name="buffer">The pixel buffer containing frame data.</param>
    internal ImageFrame(PixelBuffer buffer)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
    }

    /// <summary>
    /// Gets the pixel color at the specified coordinates.
    /// </summary>
    /// <param name="x">The x coordinate (column).</param>
    /// <param name="y">The y coordinate (row).</param>
    /// <returns>The pixel color at the specified location.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Rgba32 GetPixel(int x, int y) => _buffer.GetPixel(x, y);

    /// <summary>
    /// Sets the pixel color at the specified coordinates.
    /// </summary>
    /// <param name="x">The x coordinate (column).</param>
    /// <param name="y">The y coordinate (row).</param>
    /// <param name="color">The color to set.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetPixel(int x, int y, Rgba32 color) => _buffer.SetPixel(x, y, color);

    /// <summary>
    /// Gets the internal pixel buffer for codec access.
    /// </summary>
    internal PixelBuffer Buffer => _buffer;

    /// <summary>
    /// Gets the raw pixel data as a byte array (RGBA format).
    /// </summary>
    /// <returns>A copy of the pixel data.</returns>
    public byte[] GetPixelData() => _buffer.GetRawData();

    /// <summary>
    /// Creates a deep copy of this frame.
    /// </summary>
    /// <returns>A new frame with copied pixel data.</returns>
    public ImageFrame Clone()
    {
        return new ImageFrame(_buffer.Clone())
        {
            Duration = Duration
        };
    }
}
