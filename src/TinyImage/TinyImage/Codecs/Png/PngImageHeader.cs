using System;
using System.Collections.Generic;

namespace TinyImage.Codecs.Png;

/// <summary>
/// The high level information about the PNG image.
/// </summary>
internal readonly struct PngImageHeader
{
    internal static readonly byte[] HeaderBytes = { 73, 72, 68, 82 };

    private static readonly IReadOnlyDictionary<PngColorType, HashSet<byte>> PermittedBitDepths = new Dictionary<PngColorType, HashSet<byte>>
    {
        { PngColorType.None, new HashSet<byte> { 1, 2, 4, 8, 16 } },
        { PngColorType.ColorUsed, new HashSet<byte> { 8, 16 } },
        { PngColorType.PaletteUsed | PngColorType.ColorUsed, new HashSet<byte> { 1, 2, 4, 8 } },
        { PngColorType.AlphaChannelUsed, new HashSet<byte> { 8, 16 } },
        { PngColorType.AlphaChannelUsed | PngColorType.ColorUsed, new HashSet<byte> { 8, 16 } },
    };

    public int Width { get; }
    public int Height { get; }
    public byte BitDepth { get; }
    public PngColorType ColorType { get; }
    public PngCompressionMethod CompressionMethod { get; }
    public PngFilterMethod FilterMethod { get; }
    public PngInterlaceMethod InterlaceMethod { get; }

    public PngImageHeader(int width, int height, byte bitDepth, PngColorType colorType, 
        PngCompressionMethod compressionMethod, PngFilterMethod filterMethod, PngInterlaceMethod interlaceMethod)
    {
        if (width == 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Invalid width (0) for image.");
        if (height == 0)
            throw new ArgumentOutOfRangeException(nameof(height), "Invalid height (0) for image.");

        if (!PermittedBitDepths.TryGetValue(colorType, out var permitted) || !permitted.Contains(bitDepth))
            throw new ArgumentException($"The bit depth {bitDepth} is not permitted for color type {colorType}.");

        Width = width;
        Height = height;
        BitDepth = bitDepth;
        ColorType = colorType;
        CompressionMethod = compressionMethod;
        FilterMethod = filterMethod;
        InterlaceMethod = interlaceMethod;
    }
}
