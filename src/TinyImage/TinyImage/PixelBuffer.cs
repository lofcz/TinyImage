using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TinyImage;

/// <summary>
/// Internal buffer for storing image pixels in RGBA32 format.
/// </summary>
internal sealed class PixelBuffer
{
    private readonly byte[] _data;

    /// <summary>
    /// Gets the width of the image in pixels.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets the height of the image in pixels.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Gets the stride (bytes per row) of the image.
    /// </summary>
    public int Stride => Width * 4;

    /// <summary>
    /// Gets the raw pixel data as a span.
    /// </summary>
    public Span<byte> Data => _data;

    /// <summary>
    /// Gets the raw pixel data as a read-only span.
    /// </summary>
    public ReadOnlySpan<byte> ReadOnlyData => _data;

    /// <summary>
    /// Creates a new pixel buffer with the specified dimensions.
    /// </summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    public PixelBuffer(int width, int height)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive.");
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive.");

        Width = width;
        Height = height;
        _data = new byte[width * height * 4];
    }

    /// <summary>
    /// Creates a new pixel buffer from existing data.
    /// </summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="data">The raw RGBA32 pixel data.</param>
    public PixelBuffer(int width, int height, byte[] data)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive.");
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive.");
        if (data == null)
            throw new ArgumentNullException(nameof(data));
        if (data.Length != width * height * 4)
            throw new ArgumentException($"Data length must be {width * height * 4} bytes.", nameof(data));

        Width = width;
        Height = height;
        _data = data;
    }

    /// <summary>
    /// Gets the pixel at the specified coordinates.
    /// </summary>
    /// <param name="x">The x coordinate (column).</param>
    /// <param name="y">The y coordinate (row).</param>
    /// <returns>The pixel color.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Rgba32 GetPixel(int x, int y)
    {
        ValidateCoordinates(x, y);
        int offset = (y * Width + x) * 4;
        return new Rgba32(_data[offset], _data[offset + 1], _data[offset + 2], _data[offset + 3]);
    }

    /// <summary>
    /// Sets the pixel at the specified coordinates.
    /// </summary>
    /// <param name="x">The x coordinate (column).</param>
    /// <param name="y">The y coordinate (row).</param>
    /// <param name="color">The pixel color.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetPixel(int x, int y, Rgba32 color)
    {
        ValidateCoordinates(x, y);
        int offset = (y * Width + x) * 4;
        _data[offset] = color.R;
        _data[offset + 1] = color.G;
        _data[offset + 2] = color.B;
        _data[offset + 3] = color.A;
    }

    /// <summary>
    /// Gets a span to the row at the specified y coordinate.
    /// </summary>
    /// <param name="y">The row index.</param>
    /// <returns>A span containing the row's pixel data.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> GetRowSpan(int y)
    {
        if ((uint)y >= (uint)Height)
            throw new ArgumentOutOfRangeException(nameof(y));
        
        int offset = y * Stride;
        return _data.AsSpan(offset, Stride);
    }

    /// <summary>
    /// Gets the raw underlying byte array.
    /// </summary>
    /// <returns>The raw byte array.</returns>
    public byte[] GetRawData() => _data;

    /// <summary>
    /// Creates a copy of this pixel buffer.
    /// </summary>
    /// <returns>A new pixel buffer with copied data.</returns>
    public PixelBuffer Clone()
    {
        var copy = new byte[_data.Length];
        Array.Copy(_data, copy, _data.Length);
        return new PixelBuffer(Width, Height, copy);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ValidateCoordinates(int x, int y)
    {
        if ((uint)x >= (uint)Width)
            throw new ArgumentOutOfRangeException(nameof(x), $"X must be between 0 and {Width - 1}.");
        if ((uint)y >= (uint)Height)
            throw new ArgumentOutOfRangeException(nameof(y), $"Y must be between 0 and {Height - 1}.");
    }
}
