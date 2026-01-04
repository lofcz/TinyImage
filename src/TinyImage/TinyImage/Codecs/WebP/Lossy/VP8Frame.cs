using System;

namespace TinyImage.Codecs.WebP.Lossy;

/// <summary>
/// Represents a decoded VP8 frame in YUV format.
/// Translated from webp-rust vp8.rs:Frame
/// </summary>
internal class VP8Frame
{
    /// <summary>Width of the luma plane</summary>
    public ushort Width { get; set; }

    /// <summary>Height of the luma plane</summary>
    public ushort Height { get; set; }

    /// <summary>Luma (Y) plane</summary>
    public byte[] YBuffer { get; set; }

    /// <summary>Blue chroma (U/Cb) plane</summary>
    public byte[] UBuffer { get; set; }

    /// <summary>Red chroma (V/Cr) plane</summary>
    public byte[] VBuffer { get; set; }

    /// <summary>VP8 version</summary>
    public byte Version { get; set; }

    /// <summary>Whether this frame is for display</summary>
    public bool ForDisplay { get; set; }

    /// <summary>Pixel type (Section 9.2)</summary>
    public byte PixelType { get; set; }

    /// <summary>Filter type: true = simple, false = normal</summary>
    public bool FilterType { get; set; }

    /// <summary>Filter level</summary>
    public byte FilterLevel { get; set; }

    /// <summary>Sharpness level</summary>
    public byte SharpnessLevel { get; set; }

    public VP8Frame()
    {
        YBuffer = Array.Empty<byte>();
        UBuffer = Array.Empty<byte>();
        VBuffer = Array.Empty<byte>();
    }

    /// <summary>
    /// Chroma width (half of luma width, rounded up).
    /// </summary>
    public ushort ChromaWidth => (ushort)((Width + 1) / 2);

    /// <summary>
    /// Buffer width (padded to macroblock boundary).
    /// </summary>
    public ushort BufferWidth
    {
        get
        {
            int diff = Width % 16;
            return diff > 0 ? (ushort)(Width + (16 - diff)) : Width;
        }
    }

    /// <summary>
    /// Fills an RGB buffer from the YUV planes.
    /// </summary>
    public void FillRgb(byte[] buffer, bool useBilinear = true)
    {
        if (useBilinear)
        {
            FillRgbFancy(buffer, 3);
        }
        else
        {
            YuvConversion.FillRgbaBufferSimple(buffer, YBuffer, UBuffer, VBuffer,
                Width, ChromaWidth, BufferWidth);
        }
    }

    /// <summary>
    /// Fills an RGBA buffer from the YUV planes.
    /// </summary>
    public void FillRgba(byte[] buffer, bool useBilinear = true)
    {
        if (useBilinear)
        {
            YuvConversion.FillRgbaBufferFancy(buffer, YBuffer, UBuffer, VBuffer,
                Width, Height, BufferWidth);
        }
        else
        {
            YuvConversion.FillRgbaBufferSimple(buffer, YBuffer, UBuffer, VBuffer,
                Width, ChromaWidth, BufferWidth);
        }
    }

    private void FillRgbFancy(byte[] buffer, int bpp)
    {
        // Simplified RGB fill - convert via RGBA then strip alpha
        byte[] rgba = new byte[Width * Height * 4];
        YuvConversion.FillRgbaBufferFancy(rgba, YBuffer, UBuffer, VBuffer,
            Width, Height, BufferWidth);

        for (int i = 0; i < Width * Height; i++)
        {
            buffer[i * bpp] = rgba[i * 4];
            buffer[i * bpp + 1] = rgba[i * 4 + 1];
            buffer[i * bpp + 2] = rgba[i * 4 + 2];
        }
    }
}
