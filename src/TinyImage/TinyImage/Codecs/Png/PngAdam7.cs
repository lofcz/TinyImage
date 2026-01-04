using System.Collections.Generic;

namespace TinyImage.Codecs.Png;

/// <summary>
/// Adam7 interlacing support for PNG.
/// </summary>
internal static class PngAdam7
{
    private static readonly IReadOnlyDictionary<int, int[]> PassToScanlineGridIndex = new Dictionary<int, int[]>
    {
        { 1, new[] { 0 } },
        { 2, new[] { 0 } },
        { 3, new[] { 4 } },
        { 4, new[] { 0, 4 } },
        { 5, new[] { 2, 6 } },
        { 6, new[] { 0, 2, 4, 6 } },
        { 7, new[] { 1, 3, 5, 7 } }
    };

    private static readonly IReadOnlyDictionary<int, int[]> PassToScanlineColumnIndex = new Dictionary<int, int[]>
    {
        { 1, new[] { 0 } },
        { 2, new[] { 4 } },
        { 3, new[] { 0, 4 } },
        { 4, new[] { 2, 6 } },
        { 5, new[] { 0, 2, 4, 6 } },
        { 6, new[] { 1, 3, 5, 7 } },
        { 7, new[] { 0, 1, 2, 3, 4, 5, 6, 7 } }
    };

    public static int GetNumberOfScanlinesInPass(PngImageHeader header, int pass)
    {
        var indices = PassToScanlineGridIndex[pass + 1];
        var mod = header.Height % 8;
        var fitsExactly = mod == 0;

        if (fitsExactly)
            return indices.Length * (header.Height / 8);

        var additionalLines = 0;
        for (var i = 0; i < indices.Length; i++)
        {
            if (indices[i] < mod)
                additionalLines++;
        }

        return (indices.Length * (header.Height / 8)) + additionalLines;
    }

    public static int GetPixelsPerScanlineInPass(PngImageHeader header, int pass)
    {
        var indices = PassToScanlineColumnIndex[pass + 1];
        var mod = header.Width % 8;
        var fitsExactly = mod == 0;

        if (fitsExactly)
            return indices.Length * (header.Width / 8);

        var additionalColumns = 0;
        for (var i = 0; i < indices.Length; i++)
        {
            if (indices[i] < mod)
                additionalColumns++;
        }

        return (indices.Length * (header.Width / 8)) + additionalColumns;
    }

    public static (int x, int y) GetPixelIndexForScanlineInPass(PngImageHeader header, int pass, int scanlineIndex, int indexInScanline)
    {
        var columnIndices = PassToScanlineColumnIndex[pass + 1];
        var rows = PassToScanlineGridIndex[pass + 1];

        var actualRow = scanlineIndex % rows.Length;
        var actualCol = indexInScanline % columnIndices.Length;
        var precedingRows = 8 * (scanlineIndex / rows.Length);
        var precedingCols = 8 * (indexInScanline / columnIndices.Length);

        return (precedingCols + columnIndices[actualCol], precedingRows + rows[actualRow]);
    }
}
