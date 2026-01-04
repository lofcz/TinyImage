using System;
using System.Collections.Generic;

namespace TinyImage.Codecs.Tiff;

/// <summary>
/// Contains parsed information about a TIFF image.
/// Based on image-tiff decoder/image.rs Image struct.
/// </summary>
internal sealed class TiffImageInfo
{
    /// <summary>
    /// Image width in pixels.
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// Image height in pixels.
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// Bits per sample (component).
    /// </summary>
    public int BitsPerSample { get; set; } = 8;

    /// <summary>
    /// Number of samples (components) per pixel.
    /// </summary>
    public int SamplesPerPixel { get; set; } = 1;

    /// <summary>
    /// Photometric interpretation.
    /// </summary>
    public TiffPhotometric Photometric { get; set; } = TiffPhotometric.BlackIsZero;

    /// <summary>
    /// Compression method.
    /// </summary>
    public TiffCompression Compression { get; set; } = TiffCompression.None;

    /// <summary>
    /// Planar configuration.
    /// </summary>
    public TiffPlanarConfig PlanarConfig { get; set; } = TiffPlanarConfig.Chunky;

    /// <summary>
    /// Predictor for compression.
    /// </summary>
    public TiffPredictor Predictor { get; set; } = TiffPredictor.None;

    /// <summary>
    /// Sample format for each sample.
    /// </summary>
    public TiffSampleFormat SampleFormat { get; set; } = TiffSampleFormat.UnsignedInt;

    /// <summary>
    /// Extra samples description.
    /// </summary>
    public TiffExtraSamples[] ExtraSamples { get; set; } = Array.Empty<TiffExtraSamples>();

    /// <summary>
    /// Rows per strip (for strip-organized images).
    /// </summary>
    public int RowsPerStrip { get; set; }

    /// <summary>
    /// Strip offsets (for strip-organized images).
    /// </summary>
    public long[] StripOffsets { get; set; } = Array.Empty<long>();

    /// <summary>
    /// Strip byte counts (for strip-organized images).
    /// </summary>
    public long[] StripByteCounts { get; set; } = Array.Empty<long>();

    /// <summary>
    /// Tile width (for tile-organized images).
    /// </summary>
    public int TileWidth { get; set; }

    /// <summary>
    /// Tile height/length (for tile-organized images).
    /// </summary>
    public int TileLength { get; set; }

    /// <summary>
    /// Tile offsets (for tile-organized images).
    /// </summary>
    public long[] TileOffsets { get; set; } = Array.Empty<long>();

    /// <summary>
    /// Tile byte counts (for tile-organized images).
    /// </summary>
    public long[] TileByteCounts { get; set; } = Array.Empty<long>();

    /// <summary>
    /// Color map for palette images (R, G, B arrays).
    /// </summary>
    public ushort[]? ColorMap { get; set; }

    /// <summary>
    /// YCbCr subsampling factors [horizontal, vertical].
    /// Common values: [2, 2] for 4:2:0, [2, 1] for 4:2:2, [1, 1] for 4:4:4.
    /// </summary>
    public int[]? YCbCrSubSampling { get; set; }

    /// <summary>
    /// YCbCr coefficients [LumaRed, LumaGreen, LumaBlue].
    /// Default (ITU-R BT.601): [0.299, 0.587, 0.114].
    /// </summary>
    public float[]? YCbCrCoefficients { get; set; }

    /// <summary>
    /// JPEG tables for JPEG-compressed TIFFs.
    /// Contains quantization and Huffman tables.
    /// </summary>
    public byte[]? JpegTables { get; set; }

    /// <summary>
    /// Gets whether this image uses tiles instead of strips.
    /// </summary>
    public bool IsTiled => TileOffsets.Length > 0;

    /// <summary>
    /// Gets whether this image has an alpha channel.
    /// </summary>
    public bool HasAlpha => ExtraSamples.Length > 0 &&
        (ExtraSamples[0] == TiffExtraSamples.AssociatedAlpha ||
         ExtraSamples[0] == TiffExtraSamples.UnassociatedAlpha);

    /// <summary>
    /// Gets the number of strips in the image.
    /// </summary>
    public int StripCount => StripOffsets.Length;

    /// <summary>
    /// Gets the number of tiles in the image.
    /// </summary>
    public int TileCount => TileOffsets.Length;

    /// <summary>
    /// Gets the number of tiles across the image width.
    /// </summary>
    public int TilesAcross => TileWidth > 0 ? (Width + TileWidth - 1) / TileWidth : 0;

    /// <summary>
    /// Gets the number of tiles down the image height.
    /// </summary>
    public int TilesDown => TileLength > 0 ? (Height + TileLength - 1) / TileLength : 0;

    /// <summary>
    /// Gets the byte size of a single uncompressed row.
    /// </summary>
    public int RowByteSize => (Width * SamplesPerPixel * BitsPerSample + 7) / 8;

    /// <summary>
    /// Gets the expected uncompressed size of a strip.
    /// </summary>
    public int GetUncompressedStripSize(int stripIndex)
    {
        int rowsInStrip = Math.Min(RowsPerStrip, Height - stripIndex * RowsPerStrip);
        return rowsInStrip * RowByteSize;
    }

    /// <summary>
    /// Gets the expected uncompressed size of a tile.
    /// </summary>
    public int GetUncompressedTileSize()
    {
        int bytesPerRow = (TileWidth * SamplesPerPixel * BitsPerSample + 7) / 8;
        return bytesPerRow * TileLength;
    }

    /// <summary>
    /// Creates image info by parsing an IFD.
    /// </summary>
    public static TiffImageInfo FromIfd(TiffIfd ifd, TiffValueReader reader)
    {
        var info = new TiffImageInfo
        {
            Width = (int)ifd.GetRequiredUInt32(TiffTag.ImageWidth),
            Height = (int)ifd.GetRequiredUInt32(TiffTag.ImageLength)
        };

        if (info.Width <= 0 || info.Height <= 0)
            throw new TiffFormatException($"Invalid dimensions: {info.Width}x{info.Height}");

        // Bits per sample (default 1)
        if (ifd.TryGetEntry(TiffTag.BitsPerSample, out var bpsEntry))
        {
            var bpsValues = reader.ReadUInt16Array(bpsEntry);
            info.BitsPerSample = bpsValues.Length > 0 ? bpsValues[0] : 1;
        }
        else
        {
            info.BitsPerSample = 1;
        }

        // Samples per pixel (default 1)
        info.SamplesPerPixel = (int)(ifd.GetOptionalUInt16(TiffTag.SamplesPerPixel) ?? 1);

        // Photometric interpretation
        var photoValue = ifd.GetOptionalUInt16(TiffTag.PhotometricInterpretation);
        info.Photometric = photoValue.HasValue ? (TiffPhotometric)photoValue.Value : TiffPhotometric.BlackIsZero;

        // Compression
        var compValue = ifd.GetOptionalUInt16(TiffTag.Compression);
        info.Compression = compValue.HasValue ? (TiffCompression)compValue.Value : TiffCompression.None;

        // Planar configuration
        var planarValue = ifd.GetOptionalUInt16(TiffTag.PlanarConfiguration);
        info.PlanarConfig = planarValue.HasValue ? (TiffPlanarConfig)planarValue.Value : TiffPlanarConfig.Chunky;

        // Predictor
        var predValue = ifd.GetOptionalUInt16(TiffTag.Predictor);
        info.Predictor = predValue.HasValue ? (TiffPredictor)predValue.Value : TiffPredictor.None;

        // Sample format
        if (ifd.TryGetEntry(TiffTag.SampleFormat, out var sfEntry))
        {
            var sfValues = reader.ReadUInt16Array(sfEntry);
            info.SampleFormat = sfValues.Length > 0 ? (TiffSampleFormat)sfValues[0] : TiffSampleFormat.UnsignedInt;
        }

        // Extra samples
        if (ifd.TryGetEntry(TiffTag.ExtraSamples, out var esEntry))
        {
            var esValues = reader.ReadUInt16Array(esEntry);
            info.ExtraSamples = new TiffExtraSamples[esValues.Length];
            for (int i = 0; i < esValues.Length; i++)
                info.ExtraSamples[i] = (TiffExtraSamples)esValues[i];
        }

        // Strip or tile organization
        bool hasStrips = ifd.ContainsTag(TiffTag.StripOffsets);
        bool hasTiles = ifd.ContainsTag(TiffTag.TileOffsets);

        if (hasStrips && !hasTiles)
        {
            // Strip-based
            info.RowsPerStrip = (int)(ifd.GetOptionalUInt32(TiffTag.RowsPerStrip) ?? (uint)info.Height);

            if (ifd.TryGetEntry(TiffTag.StripOffsets, out var soEntry))
                info.StripOffsets = reader.ReadOffsetArray(soEntry);

            if (ifd.TryGetEntry(TiffTag.StripByteCounts, out var sbcEntry))
                info.StripByteCounts = reader.ReadOffsetArray(sbcEntry);
        }
        else if (hasTiles)
        {
            // Tile-based
            info.TileWidth = (int)ifd.GetRequiredUInt32(TiffTag.TileWidth);
            info.TileLength = (int)ifd.GetRequiredUInt32(TiffTag.TileLength);

            if (ifd.TryGetEntry(TiffTag.TileOffsets, out var toEntry))
                info.TileOffsets = reader.ReadOffsetArray(toEntry);

            if (ifd.TryGetEntry(TiffTag.TileByteCounts, out var tbcEntry))
                info.TileByteCounts = reader.ReadOffsetArray(tbcEntry);
        }
        else
        {
            throw new TiffFormatException("No strip or tile organization found.");
        }

        // Color map for palette images
        if (info.Photometric == TiffPhotometric.Palette && ifd.TryGetEntry(TiffTag.ColorMap, out var cmEntry))
        {
            info.ColorMap = reader.ReadUInt16Array(cmEntry);
        }

        // YCbCr-specific tags
        if (info.Photometric == TiffPhotometric.YCbCr)
        {
            // YCbCr subsampling (default [2, 2] for 4:2:0)
            if (ifd.TryGetEntry(TiffTag.YCbCrSubSampling, out var subEntry))
            {
                var subValues = reader.ReadUInt16Array(subEntry);
                if (subValues.Length >= 2)
                {
                    info.YCbCrSubSampling = new int[] { subValues[0], subValues[1] };
                }
            }
            else
            {
                info.YCbCrSubSampling = new int[] { 2, 2 };
            }

            // YCbCr coefficients (default ITU-R BT.601)
            if (ifd.TryGetEntry(TiffTag.YCbCrCoefficients, out var coeffEntry))
            {
                var coeffValues = reader.ReadRationalArray(coeffEntry);
                if (coeffValues.Length >= 3)
                {
                    info.YCbCrCoefficients = new float[] { coeffValues[0], coeffValues[1], coeffValues[2] };
                }
            }
        }

        // JPEG tables for JPEG-compressed TIFFs
        if ((info.Compression == TiffCompression.Jpeg || info.Compression == TiffCompression.OldJpeg) &&
            ifd.TryGetEntry(TiffTag.JpegTables, out var jpegTablesEntry))
        {
            info.JpegTables = reader.ReadByteArray(jpegTablesEntry);
        }

        return info;
    }
}
