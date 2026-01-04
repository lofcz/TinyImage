using System;

namespace TinyImage.Codecs.Png;

/// <summary>
/// Provides methods for extracting pixel values from raw PNG byte data.
/// </summary>
internal sealed class PngRawData
{
    private readonly byte[] _data;
    private readonly int _bytesPerPixel;
    private readonly int _width;
    private readonly PngPalette? _palette;
    private readonly PngColorType _colorType;
    private readonly int _rowOffset;
    private readonly int _bitDepth;

    public PngRawData(byte[] data, int bytesPerPixel, PngPalette? palette, PngImageHeader imageHeader)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _bytesPerPixel = bytesPerPixel;
        _palette = palette;
        _width = imageHeader.Width;
        _colorType = imageHeader.ColorType;
        _rowOffset = imageHeader.InterlaceMethod == PngInterlaceMethod.Adam7 ? 0 : 1;
        _bitDepth = imageHeader.BitDepth;
    }

    public Rgba32 GetPixel(int x, int y)
    {
        if (_palette != null)
            return GetPalettePixel(x, y);

        var rowStartPixel = (_rowOffset + (_rowOffset * y)) + (_bytesPerPixel * _width * y);
        var pixelStartIndex = rowStartPixel + (_bytesPerPixel * x);
        var first = _data[pixelStartIndex];

        return _bytesPerPixel switch
        {
            1 => new Rgba32(first, first, first, 255),
            2 => GetTwoBytePixel(first, pixelStartIndex),
            3 => new Rgba32(first, _data[pixelStartIndex + 1], _data[pixelStartIndex + 2], 255),
            4 => GetFourBytePixel(first, pixelStartIndex),
            6 => new Rgba32(first, _data[pixelStartIndex + 2], _data[pixelStartIndex + 4], 255),
            8 => new Rgba32(first, _data[pixelStartIndex + 2], _data[pixelStartIndex + 4], _data[pixelStartIndex + 6]),
            _ => throw new InvalidOperationException($"Unrecognized number of bytes per pixel: {_bytesPerPixel}.")
        };
    }

    private Rgba32 GetPalettePixel(int x, int y)
    {
        var pixelsPerByte = 8 / _bitDepth;
        var bytesInRow = 1 + (_width / pixelsPerByte);
        var byteIndexInRow = x / pixelsPerByte;
        var paletteIndex = (1 + (y * bytesInRow)) + byteIndexInRow;
        var b = _data[paletteIndex];

        if (_bitDepth == 8)
            return _palette!.GetPixel(b);

        var withinByteIndex = x % pixelsPerByte;
        var rightShift = 8 - ((withinByteIndex + 1) * _bitDepth);
        var indexActual = (b >> rightShift) & ((1 << _bitDepth) - 1);

        return _palette!.GetPixel(indexActual);
    }

    private Rgba32 GetTwoBytePixel(byte first, int pixelStartIndex)
    {
        if (_colorType == PngColorType.None)
        {
            var second = _data[pixelStartIndex + 1];
            var value = ToSingleByte(first, second);
            return new Rgba32(value, value, value, 255);
        }
        return new Rgba32(first, first, first, _data[pixelStartIndex + 1]);
    }

    private Rgba32 GetFourBytePixel(byte first, int pixelStartIndex)
    {
        if (_colorType == (PngColorType.None | PngColorType.AlphaChannelUsed))
        {
            var second = _data[pixelStartIndex + 1];
            var firstAlpha = _data[pixelStartIndex + 2];
            var secondAlpha = _data[pixelStartIndex + 3];
            var gray = ToSingleByte(first, second);
            var alpha = ToSingleByte(firstAlpha, secondAlpha);
            return new Rgba32(gray, gray, gray, alpha);
        }
        return new Rgba32(first, _data[pixelStartIndex + 1], _data[pixelStartIndex + 2], _data[pixelStartIndex + 3]);
    }

    private static byte ToSingleByte(byte first, byte second)
    {
        var us = (first << 8) + second;
        return (byte)Math.Round((255 * us) / (double)ushort.MaxValue);
    }
}
