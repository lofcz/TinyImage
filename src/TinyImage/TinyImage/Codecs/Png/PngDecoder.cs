using System;

namespace TinyImage.Codecs.Png;

/// <summary>
/// PNG filter decoding and pixel format handling.
/// </summary>
internal static class PngDecoder
{
    public static (byte bytesPerPixel, byte samplesPerPixel) GetBytesAndSamplesPerPixel(PngImageHeader header)
    {
        var bitDepthCorrected = (header.BitDepth + 7) / 8;
        var samplesPerPixel = SamplesPerPixel(header);
        return ((byte)(samplesPerPixel * bitDepthCorrected), samplesPerPixel);
    }

    public static byte[] Decode(byte[] decompressedData, PngImageHeader header, byte bytesPerPixel, byte samplesPerPixel)
    {
        switch (header.InterlaceMethod)
        {
            case PngInterlaceMethod.None:
                return DecodeNonInterlaced(decompressedData, header, bytesPerPixel, samplesPerPixel);
            case PngInterlaceMethod.Adam7:
                return DecodeAdam7(decompressedData, header, bytesPerPixel);
            default:
                throw new ArgumentOutOfRangeException($"Invalid interlace method: {header.InterlaceMethod}.");
        }
    }

    private static byte[] DecodeNonInterlaced(byte[] data, PngImageHeader header, byte bytesPerPixel, byte samplesPerPixel)
    {
        var bytesPerScanline = BytesPerScanline(header, samplesPerPixel);
        var currentRowStartByteAbsolute = 1;

        for (var rowIndex = 0; rowIndex < header.Height; rowIndex++)
        {
            var filterType = (PngFilterType)data[currentRowStartByteAbsolute - 1];
            var previousRowStartByteAbsolute = rowIndex + (bytesPerScanline * (rowIndex - 1));
            var end = currentRowStartByteAbsolute + bytesPerScanline;

            for (var currentByteAbsolute = currentRowStartByteAbsolute; currentByteAbsolute < end; currentByteAbsolute++)
            {
                ReverseFilter(data, filterType, previousRowStartByteAbsolute, currentRowStartByteAbsolute,
                    currentByteAbsolute, currentByteAbsolute - currentRowStartByteAbsolute, bytesPerPixel);
            }

            currentRowStartByteAbsolute += bytesPerScanline + 1;
        }

        return data;
    }

    private static byte[] DecodeAdam7(byte[] data, PngImageHeader header, byte bytesPerPixel)
    {
        var byteHack = bytesPerPixel == 1 ? 1 : 0;
        var pixelsPerRow = header.Width * bytesPerPixel + byteHack;
        var newBytes = new byte[header.Height * pixelsPerRow];
        var i = 0;
        var previousStartRowByteAbsolute = -1;

        for (var pass = 0; pass < 7; pass++)
        {
            var numberOfScanlines = PngAdam7.GetNumberOfScanlinesInPass(header, pass);
            var numberOfPixelsPerScanline = PngAdam7.GetPixelsPerScanlineInPass(header, pass);

            if (numberOfScanlines <= 0 || numberOfPixelsPerScanline <= 0)
                continue;

            for (var scanlineIndex = 0; scanlineIndex < numberOfScanlines; scanlineIndex++)
            {
                var filterType = (PngFilterType)data[i++];
                var rowStartByte = i;

                for (var j = 0; j < numberOfPixelsPerScanline; j++)
                {
                    var pixelIndex = PngAdam7.GetPixelIndexForScanlineInPass(header, pass, scanlineIndex, j);
                    for (var k = 0; k < bytesPerPixel; k++)
                    {
                        var byteLineNumber = (j * bytesPerPixel) + k;
                        ReverseFilter(data, filterType, previousStartRowByteAbsolute, rowStartByte, i, byteLineNumber, bytesPerPixel);
                        i++;
                    }

                    var start = byteHack + pixelsPerRow * pixelIndex.y + pixelIndex.x * bytesPerPixel;
                    Array.Copy(data, rowStartByte + j * bytesPerPixel, newBytes, start, bytesPerPixel);
                }

                previousStartRowByteAbsolute = rowStartByte;
            }
        }

        return newBytes;
    }

    private static byte SamplesPerPixel(PngImageHeader header)
    {
        return header.ColorType switch
        {
            PngColorType.None => 1,
            PngColorType.PaletteUsed => 1,
            PngColorType.PaletteUsed | PngColorType.ColorUsed => 1,
            PngColorType.ColorUsed => 3,
            PngColorType.AlphaChannelUsed => 2,
            PngColorType.ColorUsed | PngColorType.AlphaChannelUsed => 4,
            _ => 0
        };
    }

    private static int BytesPerScanline(PngImageHeader header, byte samplesPerPixel)
    {
        var width = header.Width;
        return header.BitDepth switch
        {
            1 => (width + 7) / 8,
            2 => (width + 3) / 4,
            4 => (width + 1) / 2,
            8 or 16 => width * samplesPerPixel * (header.BitDepth / 8),
            _ => 0
        };
    }

    private static void ReverseFilter(byte[] data, PngFilterType type, int previousRowStartByteAbsolute,
        int rowStartByteAbsolute, int byteAbsolute, int rowByteIndex, int bytesPerPixel)
    {
        byte GetLeftByteValue()
        {
            var leftIndex = rowByteIndex - bytesPerPixel;
            return leftIndex >= 0 ? data[rowStartByteAbsolute + leftIndex] : (byte)0;
        }

        byte GetAboveByteValue()
        {
            var upIndex = previousRowStartByteAbsolute + rowByteIndex;
            return upIndex >= 0 ? data[upIndex] : (byte)0;
        }

        byte GetAboveLeftByteValue()
        {
            var index = previousRowStartByteAbsolute + rowByteIndex - bytesPerPixel;
            return index < previousRowStartByteAbsolute || previousRowStartByteAbsolute < 0 ? (byte)0 : data[index];
        }

        if (type == PngFilterType.Up)
        {
            var above = previousRowStartByteAbsolute + rowByteIndex;
            if (above < 0) return;
            data[byteAbsolute] += data[above];
            return;
        }

        if (type == PngFilterType.Sub)
        {
            var leftIndex = rowByteIndex - bytesPerPixel;
            if (leftIndex < 0) return;
            data[byteAbsolute] += data[rowStartByteAbsolute + leftIndex];
            return;
        }

        switch (type)
        {
            case PngFilterType.None:
                return;
            case PngFilterType.Average:
                data[byteAbsolute] += (byte)((GetLeftByteValue() + GetAboveByteValue()) / 2);
                break;
            case PngFilterType.Paeth:
                var a = GetLeftByteValue();
                var b = GetAboveByteValue();
                var c = GetAboveLeftByteValue();
                data[byteAbsolute] += GetPaethValue(a, b, c);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }
    }

    private static byte GetPaethValue(byte a, byte b, byte c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);

        if (pa <= pb && pa <= pc)
            return a;

        return pb <= pc ? b : c;
    }
}
