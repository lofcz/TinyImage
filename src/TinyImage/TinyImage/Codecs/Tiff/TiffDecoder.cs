using System;
using System.Collections.Generic;
using System.IO;

namespace TinyImage.Codecs.Tiff;

/// <summary>
/// Decodes TIFF images.
/// Based on image-tiff decoder/mod.rs.
/// </summary>
internal sealed class TiffDecoder
{
    private readonly Stream _stream;
    private TiffByteOrder _byteOrder = null!;
    private bool _isBigTiff;
    private long _firstIfdOffset;
    private TiffValueReader _valueReader = null!;

    /// <summary>
    /// Clamps a value between min and max (netstandard2.0 compatible).
    /// </summary>
    private static float Clamp(float value, float min, float max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    /// <summary>
    /// Clamps a value between min and max (netstandard2.0 compatible).
    /// </summary>
    private static double Clamp(double value, double min, double max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    /// <summary>
    /// Creates a new TIFF decoder for the given stream.
    /// </summary>
    public TiffDecoder(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    /// <summary>
    /// Decodes the TIFF file and returns all images as a list of Image objects.
    /// </summary>
    public List<Image> Decode()
    {
        ReadHeader();

        var images = new List<Image>();
        long ifdOffset = _firstIfdOffset;

        while (ifdOffset != 0)
        {
            var ifd = ReadIfd(ifdOffset);
            var imageInfo = TiffImageInfo.FromIfd(ifd, _valueReader);
            var image = DecodeImage(imageInfo);
            images.Add(image);

            ifdOffset = ifd.NextIfdOffset;
        }

        if (images.Count == 0)
            throw new TiffFormatException("No images found in TIFF file.");

        return images;
    }

    /// <summary>
    /// Decodes only the first image from the TIFF file.
    /// </summary>
    public Image DecodeFirstImage()
    {
        ReadHeader();

        if (_firstIfdOffset == 0)
            throw new TiffFormatException("No IFD found in TIFF file.");

        var ifd = ReadIfd(_firstIfdOffset);
        var imageInfo = TiffImageInfo.FromIfd(ifd, _valueReader);
        return DecodeImage(imageInfo);
    }

    /// <summary>
    /// Reads and validates the TIFF header.
    /// </summary>
    private void ReadHeader()
    {
        // Read byte order marker (2 bytes)
        var header = new byte[8];
        if (ReadFully(_stream, header, 0, 2) != 2)
            throw new TiffFormatException("Unable to read TIFF header.");

        ushort byteOrderMarker = (ushort)(header[0] | (header[1] << 8));

        if (byteOrderMarker == TiffConstants.LittleEndianMarker)
        {
            _byteOrder = new TiffByteOrder(true);
        }
        else if (byteOrderMarker == TiffConstants.BigEndianMarker)
        {
            _byteOrder = new TiffByteOrder(false);
        }
        else
        {
            throw new TiffFormatException($"Invalid TIFF byte order marker: 0x{byteOrderMarker:X4}");
        }

        // Read magic number (2 bytes)
        if (ReadFully(_stream, header, 0, 2) != 2)
            throw new TiffFormatException("Unable to read TIFF magic number.");

        ushort magic = _byteOrder.ReadUInt16(header.AsSpan(0, 2));

        if (magic == TiffConstants.TiffMagic)
        {
            _isBigTiff = false;

            // Read first IFD offset (4 bytes)
            if (ReadFully(_stream, header, 0, 4) != 4)
                throw new TiffFormatException("Unable to read first IFD offset.");

            _firstIfdOffset = _byteOrder.ReadUInt32(header.AsSpan(0, 4));
        }
        else if (magic == TiffConstants.BigTiffMagic)
        {
            _isBigTiff = true;

            // Read additional BigTIFF header fields
            if (ReadFully(_stream, header, 0, 4) != 4)
                throw new TiffFormatException("Unable to read BigTIFF header.");

            ushort offsetByteSize = _byteOrder.ReadUInt16(header.AsSpan(0, 2));
            if (offsetByteSize != 8)
                throw new TiffFormatException($"Invalid BigTIFF offset byte size: {offsetByteSize}");

            ushort zero = _byteOrder.ReadUInt16(header.AsSpan(2, 2));
            if (zero != 0)
                throw new TiffFormatException("Invalid BigTIFF header padding.");

            // Read first IFD offset (8 bytes)
            if (ReadFully(_stream, header, 0, 8) != 8)
                throw new TiffFormatException("Unable to read BigTIFF first IFD offset.");

            _firstIfdOffset = (long)_byteOrder.ReadUInt64(header);
        }
        else
        {
            throw new TiffFormatException($"Invalid TIFF magic number: {magic}");
        }

        _valueReader = new TiffValueReader(_stream, _byteOrder, _isBigTiff);
    }

    /// <summary>
    /// Reads an IFD at the specified offset.
    /// </summary>
    private TiffIfd ReadIfd(long offset)
    {
        _stream.Position = offset;

        var ifd = new TiffIfd();

        // Read entry count
        int entryCount;
        if (_isBigTiff)
        {
            var buffer = new byte[8];
            if (ReadFully(_stream, buffer, 0, 8) != 8)
                throw new TiffFormatException("Unable to read IFD entry count.");
            entryCount = (int)_byteOrder.ReadUInt64(buffer);
        }
        else
        {
            var buffer = new byte[2];
            if (ReadFully(_stream, buffer, 0, 2) != 2)
                throw new TiffFormatException("Unable to read IFD entry count.");
            entryCount = _byteOrder.ReadUInt16(buffer);
        }

        // Read entries
        int entrySize = _isBigTiff ? TiffConstants.BigTiffIfdEntrySize : TiffConstants.IfdEntrySize;
        var entryBuffer = new byte[entrySize];

        for (int i = 0; i < entryCount; i++)
        {
            if (ReadFully(_stream, entryBuffer, 0, entrySize) != entrySize)
                throw new TiffFormatException($"Unable to read IFD entry {i}.");

            var entry = ParseIfdEntry(entryBuffer);
            if (entry.HasValue)
                ifd.AddEntry(entry.Value);
        }

        // Read next IFD offset
        if (_isBigTiff)
        {
            var buffer = new byte[8];
            if (ReadFully(_stream, buffer, 0, 8) != 8)
                throw new TiffFormatException("Unable to read next IFD offset.");
            ifd.NextIfdOffset = (long)_byteOrder.ReadUInt64(buffer);
        }
        else
        {
            var buffer = new byte[4];
            if (ReadFully(_stream, buffer, 0, 4) != 4)
                throw new TiffFormatException("Unable to read next IFD offset.");
            ifd.NextIfdOffset = _byteOrder.ReadUInt32(buffer);
        }

        return ifd;
    }

    /// <summary>
    /// Parses a single IFD entry from buffer.
    /// </summary>
    private TiffIfdEntry? ParseIfdEntry(byte[] buffer)
    {
        var tag = (TiffTag)_byteOrder.ReadUInt16(buffer.AsSpan(0, 2));
        var fieldTypeRaw = _byteOrder.ReadUInt16(buffer.AsSpan(2, 2));

        // Validate field type
        if (fieldTypeRaw < 1 || fieldTypeRaw > 18)
        {
            // Unknown field type, skip this entry
            return null;
        }

        var fieldType = (TiffFieldType)fieldTypeRaw;

        uint count;
        ulong valueOffset;

        if (_isBigTiff)
        {
            count = (uint)_byteOrder.ReadUInt64(buffer.AsSpan(4, 8));
            valueOffset = _byteOrder.ReadUInt64(buffer.AsSpan(12, 8));
        }
        else
        {
            count = _byteOrder.ReadUInt32(buffer.AsSpan(4, 4));
            valueOffset = _byteOrder.ReadUInt32(buffer.AsSpan(8, 4));
        }

        return new TiffIfdEntry(tag, fieldType, count, valueOffset);
    }

    /// <summary>
    /// Decodes an image from the given image info.
    /// </summary>
    private Image DecodeImage(TiffImageInfo info)
    {
        // Validate support
        ValidateImageSupport(info);

        // Read raw pixel data
        byte[] rawData;

        if (info.IsTiled)
        {
            rawData = ReadTiledData(info);
        }
        else
        {
            rawData = ReadStrippedData(info);
        }

        // Apply predictor if needed
        if (info.Predictor == TiffPredictor.Horizontal)
        {
            TiffPredictorHelper.ApplyHorizontalPredictor(rawData, info);
        }
        else if (info.Predictor == TiffPredictor.FloatingPoint)
        {
            rawData = TiffPredictorHelper.ApplyFloatingPointPredictor(rawData, info);
        }

        // Convert to RGBA32
        var buffer = ConvertToRgba32(rawData, info);
        return new Image(buffer, info.HasAlpha);
    }

    /// <summary>
    /// Validates that the image uses supported features.
    /// </summary>
    private void ValidateImageSupport(TiffImageInfo info)
    {
        // Check compression support
        switch (info.Compression)
        {
            case TiffCompression.None:
            case TiffCompression.Lzw:
            case TiffCompression.Deflate:
            case TiffCompression.OldDeflate:
            case TiffCompression.PackBits:
            case TiffCompression.Jpeg:
            case TiffCompression.OldJpeg:
            case TiffCompression.Fax3:
            case TiffCompression.Fax4:
                break;
            default:
                throw new TiffUnsupportedException($"Compression method {info.Compression} is not supported.");
        }

        // Check bit depth support
        if (info.BitsPerSample != 1 && info.BitsPerSample != 8 && info.BitsPerSample != 16 &&
            info.BitsPerSample != 32 && info.BitsPerSample != 64)
        {
            throw new TiffUnsupportedException($"Bit depth {info.BitsPerSample} is not supported.");
        }

        // Planar configuration is now supported - will be converted to chunky during decoding

        // Check photometric interpretation
        switch (info.Photometric)
        {
            case TiffPhotometric.WhiteIsZero:
            case TiffPhotometric.BlackIsZero:
            case TiffPhotometric.Rgb:
            case TiffPhotometric.Palette:
            case TiffPhotometric.Cmyk:
            case TiffPhotometric.YCbCr:
                break;
            default:
                throw new TiffUnsupportedException($"Photometric interpretation {info.Photometric} is not supported.");
        }
    }

    /// <summary>
    /// Reads strip-organized image data.
    /// </summary>
    private byte[] ReadStrippedData(TiffImageInfo info)
    {
        if (info.PlanarConfig == TiffPlanarConfig.Planar && info.SamplesPerPixel > 1)
        {
            return ReadStrippedDataPlanar(info);
        }

        bool needsEnhancedDecompress = info.Compression == TiffCompression.Jpeg ||
                                       info.Compression == TiffCompression.OldJpeg ||
                                       info.Compression == TiffCompression.Fax3 ||
                                       info.Compression == TiffCompression.Fax4;

        int totalSize = info.Height * info.RowByteSize;
        var result = new byte[totalSize];

        int rowsPerStrip = info.RowsPerStrip > 0 ? info.RowsPerStrip : info.Height;
        int offset = 0;

        for (int i = 0; i < info.StripCount; i++)
        {
            int rowsInStrip = Math.Min(rowsPerStrip, info.Height - i * rowsPerStrip);
            int uncompressedSize = info.GetUncompressedStripSize(i);

            byte[] stripData;
            if (needsEnhancedDecompress)
            {
                stripData = ReadAndDecompress(info.StripOffsets[i], (int)info.StripByteCounts[i],
                                              uncompressedSize, info.Compression, info,
                                              info.Width, rowsInStrip);
            }
            else
            {
                stripData = ReadAndDecompress(info.StripOffsets[i], (int)info.StripByteCounts[i],
                                              uncompressedSize, info.Compression);
            }

            int copySize = Math.Min(stripData.Length, totalSize - offset);
            Buffer.BlockCopy(stripData, 0, result, offset, copySize);
            offset += copySize;
        }

        return result;
    }

    /// <summary>
    /// Reads strip-organized image data with planar configuration.
    /// Each plane (color component) is stored in separate strips.
    /// </summary>
    private byte[] ReadStrippedDataPlanar(TiffImageInfo info)
    {
        int samplesPerPixel = info.SamplesPerPixel;
        int bytesPerSample = (info.BitsPerSample + 7) / 8;
        int pixelCount = info.Width * info.Height;
        int planeSize = pixelCount * bytesPerSample;

        // Allocate buffers for each plane
        var planes = new byte[samplesPerPixel][];
        for (int p = 0; p < samplesPerPixel; p++)
        {
            planes[p] = new byte[planeSize];
        }

        // Calculate strips per plane
        int rowsPerStrip = info.RowsPerStrip > 0 ? info.RowsPerStrip : info.Height;
        int stripsPerPlane = (info.Height + rowsPerStrip - 1) / rowsPerStrip;
        int bytesPerRow = info.Width * bytesPerSample;

        // Read each plane's strips
        for (int p = 0; p < samplesPerPixel; p++)
        {
            int planeOffset = 0;
            for (int s = 0; s < stripsPerPlane; s++)
            {
                int stripIndex = p * stripsPerPlane + s;
                if (stripIndex >= info.StripCount) break;

                int rowsInStrip = Math.Min(rowsPerStrip, info.Height - s * rowsPerStrip);
                int uncompressedSize = rowsInStrip * bytesPerRow;

                var stripData = ReadAndDecompress(info.StripOffsets[stripIndex], 
                                                   (int)info.StripByteCounts[stripIndex],
                                                   uncompressedSize, info.Compression);

                int copySize = Math.Min(stripData.Length, planeSize - planeOffset);
                Buffer.BlockCopy(stripData, 0, planes[p], planeOffset, copySize);
                planeOffset += copySize;
            }
        }

        // Interleave planes into chunky format
        return InterleavePlanes(planes, pixelCount, bytesPerSample);
    }

    /// <summary>
    /// Reads tile-organized image data.
    /// </summary>
    private byte[] ReadTiledData(TiffImageInfo info)
    {
        if (info.PlanarConfig == TiffPlanarConfig.Planar && info.SamplesPerPixel > 1)
        {
            return ReadTiledDataPlanar(info);
        }

        bool needsEnhancedDecompress = info.Compression == TiffCompression.Jpeg ||
                                       info.Compression == TiffCompression.OldJpeg ||
                                       info.Compression == TiffCompression.Fax3 ||
                                       info.Compression == TiffCompression.Fax4;

        int rowByteSize = info.RowByteSize;
        var result = new byte[info.Height * rowByteSize];

        int tilesAcross = info.TilesAcross;
        int tilesDown = info.TilesDown;
        int tileUncompressedSize = info.GetUncompressedTileSize();
        int tileBytesPerRow = (info.TileWidth * info.SamplesPerPixel * info.BitsPerSample + 7) / 8;

        for (int ty = 0; ty < tilesDown; ty++)
        {
            for (int tx = 0; tx < tilesAcross; tx++)
            {
                int tileIndex = ty * tilesAcross + tx;
                int tileWidth = Math.Min(info.TileWidth, info.Width - tx * info.TileWidth);
                int tileHeight = Math.Min(info.TileLength, info.Height - ty * info.TileLength);

                byte[] tileData;
                if (needsEnhancedDecompress)
                {
                    tileData = ReadAndDecompress(info.TileOffsets[tileIndex],
                                                  (int)info.TileByteCounts[tileIndex],
                                                  tileUncompressedSize, info.Compression, info,
                                                  tileWidth, tileHeight);
                }
                else
                {
                    tileData = ReadAndDecompress(info.TileOffsets[tileIndex],
                                                  (int)info.TileByteCounts[tileIndex],
                                                  tileUncompressedSize, info.Compression);
                }

                // Copy tile data to result
                int startY = ty * info.TileLength;
                int startX = tx * tileBytesPerRow;
                int rowsInTile = Math.Min(info.TileLength, info.Height - startY);
                int bytesToCopy = Math.Min(tileBytesPerRow, rowByteSize - startX);

                for (int row = 0; row < rowsInTile; row++)
                {
                    int srcOffset = row * tileBytesPerRow;
                    int dstOffset = (startY + row) * rowByteSize + startX;

                    if (srcOffset + bytesToCopy <= tileData.Length && dstOffset + bytesToCopy <= result.Length)
                    {
                        Buffer.BlockCopy(tileData, srcOffset, result, dstOffset, bytesToCopy);
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Reads tile-organized image data with planar configuration.
    /// Each plane (color component) is stored in separate tiles.
    /// </summary>
    private byte[] ReadTiledDataPlanar(TiffImageInfo info)
    {
        int samplesPerPixel = info.SamplesPerPixel;
        int bytesPerSample = (info.BitsPerSample + 7) / 8;
        int pixelCount = info.Width * info.Height;
        int planeSize = pixelCount * bytesPerSample;

        // Allocate buffers for each plane
        var planes = new byte[samplesPerPixel][];
        for (int p = 0; p < samplesPerPixel; p++)
        {
            planes[p] = new byte[planeSize];
        }

        int tilesAcross = info.TilesAcross;
        int tilesDown = info.TilesDown;
        int tilesPerPlane = tilesAcross * tilesDown;
        int tileBytesPerRow = (info.TileWidth * bytesPerSample);
        int tileUncompressedSize = info.TileWidth * info.TileLength * bytesPerSample;
        int planeBytesPerRow = info.Width * bytesPerSample;

        // Read each plane's tiles
        for (int p = 0; p < samplesPerPixel; p++)
        {
            for (int ty = 0; ty < tilesDown; ty++)
            {
                for (int tx = 0; tx < tilesAcross; tx++)
                {
                    int tileIndexInPlane = ty * tilesAcross + tx;
                    int tileIndex = p * tilesPerPlane + tileIndexInPlane;

                    if (tileIndex >= info.TileCount) continue;

                    var tileData = ReadAndDecompress(info.TileOffsets[tileIndex],
                                                      (int)info.TileByteCounts[tileIndex],
                                                      tileUncompressedSize, info.Compression);

                    // Copy tile data to plane
                    int startY = ty * info.TileLength;
                    int startX = tx * info.TileWidth;
                    int rowsInTile = Math.Min(info.TileLength, info.Height - startY);
                    int pixelsInRow = Math.Min(info.TileWidth, info.Width - startX);
                    int bytesToCopy = pixelsInRow * bytesPerSample;

                    for (int row = 0; row < rowsInTile; row++)
                    {
                        int srcOffset = row * tileBytesPerRow;
                        int dstOffset = (startY + row) * planeBytesPerRow + startX * bytesPerSample;

                        if (srcOffset + bytesToCopy <= tileData.Length && dstOffset + bytesToCopy <= planeSize)
                        {
                            Buffer.BlockCopy(tileData, srcOffset, planes[p], dstOffset, bytesToCopy);
                        }
                    }
                }
            }
        }

        // Interleave planes into chunky format
        return InterleavePlanes(planes, pixelCount, bytesPerSample);
    }

    /// <summary>
    /// Interleaves separate color planes into chunky (interleaved) format.
    /// Converts [RRR...][GGG...][BBB...] to [RGB][RGB][RGB]...
    /// </summary>
    private static byte[] InterleavePlanes(byte[][] planes, int pixelCount, int bytesPerSample)
    {
        int numPlanes = planes.Length;
        var result = new byte[pixelCount * numPlanes * bytesPerSample];

        for (int i = 0; i < pixelCount; i++)
        {
            for (int p = 0; p < numPlanes; p++)
            {
                int srcOffset = i * bytesPerSample;
                int dstOffset = (i * numPlanes + p) * bytesPerSample;

                for (int b = 0; b < bytesPerSample; b++)
                {
                    if (srcOffset + b < planes[p].Length && dstOffset + b < result.Length)
                    {
                        result[dstOffset + b] = planes[p][srcOffset + b];
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Reads compressed data from the stream and decompresses it.
    /// </summary>
    private byte[] ReadAndDecompress(long offset, int compressedSize, int uncompressedSize, TiffCompression compression)
    {
        _stream.Position = offset;
        var compressedData = new byte[compressedSize];
        ReadFully(_stream, compressedData, 0, compressedSize);

        return compression switch
        {
            TiffCompression.None => compressedData,
            TiffCompression.Lzw => TiffLzwDecoder.Decode(compressedData, uncompressedSize),
            TiffCompression.Deflate or TiffCompression.OldDeflate => TiffDeflateDecoder.Decode(compressedData, uncompressedSize),
            TiffCompression.PackBits => TiffPackBitsDecoder.Decode(compressedData, uncompressedSize),
            _ => throw new TiffUnsupportedException($"Compression {compression} is not supported.")
        };
    }

    /// <summary>
    /// Reads compressed data from the stream and decompresses it (with image info for JPEG).
    /// </summary>
    private byte[] ReadAndDecompress(long offset, int compressedSize, int uncompressedSize, 
                                     TiffCompression compression, TiffImageInfo info, 
                                     int expectedWidth, int expectedHeight)
    {
        _stream.Position = offset;
        var compressedData = new byte[compressedSize];
        ReadFully(_stream, compressedData, 0, compressedSize);

        return compression switch
        {
            TiffCompression.None => compressedData,
            TiffCompression.Lzw => TiffLzwDecoder.Decode(compressedData, uncompressedSize),
            TiffCompression.Deflate or TiffCompression.OldDeflate => TiffDeflateDecoder.Decode(compressedData, uncompressedSize),
            TiffCompression.PackBits => TiffPackBitsDecoder.Decode(compressedData, uncompressedSize),
            TiffCompression.Jpeg or TiffCompression.OldJpeg => DecodeJpegStrip(compressedData, info, expectedWidth, expectedHeight),
            TiffCompression.Fax3 => TiffFax3Decoder.Decode(compressedData, expectedWidth, expectedHeight),
            TiffCompression.Fax4 => TiffFax4Decoder.Decode(compressedData, expectedWidth, expectedHeight),
            _ => throw new TiffUnsupportedException($"Compression {compression} is not supported.")
        };
    }

    /// <summary>
    /// Decodes a JPEG-compressed strip or tile.
    /// </summary>
    private byte[] DecodeJpegStrip(byte[] compressedData, TiffImageInfo info, int width, int height)
    {
        var jpegDecoder = new TiffJpegDecoder(info.JpegTables);
        return jpegDecoder.Decode(compressedData, width, height);
    }

    /// <summary>
    /// Converts raw pixel data to RGBA32 format.
    /// </summary>
    private PixelBuffer ConvertToRgba32(byte[] data, TiffImageInfo info)
    {
        var buffer = new PixelBuffer(info.Width, info.Height);

        switch (info.Photometric)
        {
            case TiffPhotometric.WhiteIsZero:
                ConvertGrayscale(data, buffer, info, invert: true);
                break;

            case TiffPhotometric.BlackIsZero:
                ConvertGrayscale(data, buffer, info, invert: false);
                break;

            case TiffPhotometric.Rgb:
                ConvertRgb(data, buffer, info);
                break;

            case TiffPhotometric.Palette:
                ConvertPalette(data, buffer, info);
                break;

            case TiffPhotometric.Cmyk:
                ConvertCmyk(data, buffer, info);
                break;

            case TiffPhotometric.YCbCr:
                ConvertYCbCr(data, buffer, info);
                break;

            default:
                throw new TiffUnsupportedException($"Photometric {info.Photometric} is not supported.");
        }

        return buffer;
    }

    private void ConvertGrayscale(byte[] data, PixelBuffer buffer, TiffImageInfo info, bool invert)
    {
        int width = info.Width;
        int height = info.Height;
        int bitsPerSample = info.BitsPerSample;
        bool hasAlpha = info.SamplesPerPixel >= 2 && info.HasAlpha;

        if (bitsPerSample == 8)
        {
            int samplesPerPixel = info.SamplesPerPixel;
            int index = 0;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    byte gray = data[index];
                    if (invert) gray = (byte)(255 - gray);
                    byte alpha = hasAlpha && index + 1 < data.Length ? data[index + 1] : (byte)255;

                    buffer.SetPixel(x, y, new Rgba32(gray, gray, gray, alpha));
                    index += samplesPerPixel;
                }
            }
        }
        else if (bitsPerSample == 16)
        {
            int samplesPerPixel = info.SamplesPerPixel;
            int index = 0;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (index + 1 >= data.Length) break;
                    ushort gray16 = (ushort)(data[index] | (data[index + 1] << 8));
                    byte gray = (byte)(gray16 >> 8);
                    if (invert) gray = (byte)(255 - gray);
                    byte alpha = 255;

                    if (hasAlpha && index + 3 < data.Length)
                    {
                        ushort alpha16 = (ushort)(data[index + 2] | (data[index + 3] << 8));
                        alpha = (byte)(alpha16 >> 8);
                    }

                    buffer.SetPixel(x, y, new Rgba32(gray, gray, gray, alpha));
                    index += samplesPerPixel * 2;
                }
            }
        }
        else if (bitsPerSample == 32)
        {
            int samplesPerPixel = info.SamplesPerPixel;
            int index = 0;
            bool isFloat = info.SampleFormat == TiffSampleFormat.Float;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (index + 3 >= data.Length) break;

                    byte gray;
                    if (isFloat)
                    {
                        float value = BitConverter.ToSingle(data, index);
                        gray = (byte)Clamp(value * 255f, 0, 255);
                    }
                    else
                    {
                        uint gray32 = (uint)(data[index] | (data[index + 1] << 8) | 
                                            (data[index + 2] << 16) | (data[index + 3] << 24));
                        gray = (byte)(gray32 >> 24);
                    }

                    if (invert) gray = (byte)(255 - gray);
                    byte alpha = 255;

                    if (hasAlpha && index + 7 < data.Length)
                    {
                        if (isFloat)
                        {
                            float alphaValue = BitConverter.ToSingle(data, index + 4);
                            alpha = (byte)Clamp(alphaValue * 255f, 0, 255);
                        }
                        else
                        {
                            uint alpha32 = (uint)(data[index + 4] | (data[index + 5] << 8) | 
                                                  (data[index + 6] << 16) | (data[index + 7] << 24));
                            alpha = (byte)(alpha32 >> 24);
                        }
                    }

                    buffer.SetPixel(x, y, new Rgba32(gray, gray, gray, alpha));
                    index += samplesPerPixel * 4;
                }
            }
        }
        else if (bitsPerSample == 64)
        {
            int samplesPerPixel = info.SamplesPerPixel;
            int index = 0;
            bool isFloat = info.SampleFormat == TiffSampleFormat.Float;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (index + 7 >= data.Length) break;

                    byte gray;
                    if (isFloat)
                    {
                        double value = BitConverter.ToDouble(data, index);
                        gray = (byte)Clamp(value * 255.0, 0, 255);
                    }
                    else
                    {
                        ulong gray64 = BitConverter.ToUInt64(data, index);
                        gray = (byte)(gray64 >> 56);
                    }

                    if (invert) gray = (byte)(255 - gray);
                    byte alpha = 255;

                    if (hasAlpha && index + 15 < data.Length)
                    {
                        if (isFloat)
                        {
                            double alphaValue = BitConverter.ToDouble(data, index + 8);
                            alpha = (byte)Clamp(alphaValue * 255.0, 0, 255);
                        }
                        else
                        {
                            ulong alpha64 = BitConverter.ToUInt64(data, index + 8);
                            alpha = (byte)(alpha64 >> 56);
                        }
                    }

                    buffer.SetPixel(x, y, new Rgba32(gray, gray, gray, alpha));
                    index += samplesPerPixel * 8;
                }
            }
        }
        else if (bitsPerSample == 1)
        {
            // 1-bit bilevel
            int byteIndex = 0;
            int bitIndex = 0;

            for (int y = 0; y < height; y++)
            {
                bitIndex = 0;
                byteIndex = y * ((width + 7) / 8);

                for (int x = 0; x < width; x++)
                {
                    if (byteIndex >= data.Length) break;
                    int bit = (data[byteIndex] >> (7 - bitIndex)) & 1;
                    byte gray = bit == 1 ? (byte)255 : (byte)0;
                    if (invert) gray = (byte)(255 - gray);

                    buffer.SetPixel(x, y, new Rgba32(gray, gray, gray, 255));

                    bitIndex++;
                    if (bitIndex >= 8)
                    {
                        bitIndex = 0;
                        byteIndex++;
                    }
                }
            }
        }
    }

    private void ConvertRgb(byte[] data, PixelBuffer buffer, TiffImageInfo info)
    {
        int width = info.Width;
        int height = info.Height;
        int bitsPerSample = info.BitsPerSample;
        int samplesPerPixel = info.SamplesPerPixel;
        bool hasAlpha = samplesPerPixel >= 4;

        if (bitsPerSample == 8)
        {
            int index = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (index + 2 >= data.Length) break;

                    byte r = data[index];
                    byte g = data[index + 1];
                    byte b = data[index + 2];
                    byte a = hasAlpha && index + 3 < data.Length ? data[index + 3] : (byte)255;

                    buffer.SetPixel(x, y, new Rgba32(r, g, b, a));
                    index += samplesPerPixel;
                }
            }
        }
        else if (bitsPerSample == 16)
        {
            int index = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (index + 5 >= data.Length) break;

                    ushort r16 = (ushort)(data[index] | (data[index + 1] << 8));
                    ushort g16 = (ushort)(data[index + 2] | (data[index + 3] << 8));
                    ushort b16 = (ushort)(data[index + 4] | (data[index + 5] << 8));

                    byte r = (byte)(r16 >> 8);
                    byte g = (byte)(g16 >> 8);
                    byte b = (byte)(b16 >> 8);
                    byte a = 255;

                    if (hasAlpha && index + 7 < data.Length)
                    {
                        ushort a16 = (ushort)(data[index + 6] | (data[index + 7] << 8));
                        a = (byte)(a16 >> 8);
                    }

                    buffer.SetPixel(x, y, new Rgba32(r, g, b, a));
                    index += samplesPerPixel * 2;
                }
            }
        }
        else if (bitsPerSample == 32)
        {
            int index = 0;
            bool isFloat = info.SampleFormat == TiffSampleFormat.Float;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (index + 11 >= data.Length) break;

                    byte r, g, b, a;

                    if (isFloat)
                    {
                        float rf = BitConverter.ToSingle(data, index);
                        float gf = BitConverter.ToSingle(data, index + 4);
                        float bf = BitConverter.ToSingle(data, index + 8);

                        r = (byte)Clamp(rf * 255f, 0, 255);
                        g = (byte)Clamp(gf * 255f, 0, 255);
                        b = (byte)Clamp(bf * 255f, 0, 255);
                        a = 255;

                        if (hasAlpha && index + 15 < data.Length)
                        {
                            float af = BitConverter.ToSingle(data, index + 12);
                            a = (byte)Clamp(af * 255f, 0, 255);
                        }
                    }
                    else
                    {
                        uint r32 = BitConverter.ToUInt32(data, index);
                        uint g32 = BitConverter.ToUInt32(data, index + 4);
                        uint b32 = BitConverter.ToUInt32(data, index + 8);

                        r = (byte)(r32 >> 24);
                        g = (byte)(g32 >> 24);
                        b = (byte)(b32 >> 24);
                        a = 255;

                        if (hasAlpha && index + 15 < data.Length)
                        {
                            uint a32 = BitConverter.ToUInt32(data, index + 12);
                            a = (byte)(a32 >> 24);
                        }
                    }

                    buffer.SetPixel(x, y, new Rgba32(r, g, b, a));
                    index += samplesPerPixel * 4;
                }
            }
        }
        else if (bitsPerSample == 64)
        {
            int index = 0;
            bool isFloat = info.SampleFormat == TiffSampleFormat.Float;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (index + 23 >= data.Length) break;

                    byte r, g, b, a;

                    if (isFloat)
                    {
                        double rf = BitConverter.ToDouble(data, index);
                        double gf = BitConverter.ToDouble(data, index + 8);
                        double bf = BitConverter.ToDouble(data, index + 16);

                        r = (byte)Clamp(rf * 255.0, 0, 255);
                        g = (byte)Clamp(gf * 255.0, 0, 255);
                        b = (byte)Clamp(bf * 255.0, 0, 255);
                        a = 255;

                        if (hasAlpha && index + 31 < data.Length)
                        {
                            double af = BitConverter.ToDouble(data, index + 24);
                            a = (byte)Clamp(af * 255.0, 0, 255);
                        }
                    }
                    else
                    {
                        ulong r64 = BitConverter.ToUInt64(data, index);
                        ulong g64 = BitConverter.ToUInt64(data, index + 8);
                        ulong b64 = BitConverter.ToUInt64(data, index + 16);

                        r = (byte)(r64 >> 56);
                        g = (byte)(g64 >> 56);
                        b = (byte)(b64 >> 56);
                        a = 255;

                        if (hasAlpha && index + 31 < data.Length)
                        {
                            ulong a64 = BitConverter.ToUInt64(data, index + 24);
                            a = (byte)(a64 >> 56);
                        }
                    }

                    buffer.SetPixel(x, y, new Rgba32(r, g, b, a));
                    index += samplesPerPixel * 8;
                }
            }
        }
    }

    private void ConvertCmyk(byte[] data, PixelBuffer buffer, TiffImageInfo info)
    {
        int width = info.Width;
        int height = info.Height;
        int bitsPerSample = info.BitsPerSample;
        int samplesPerPixel = info.SamplesPerPixel;
        bool hasAlpha = samplesPerPixel >= 5;

        if (bitsPerSample == 8)
        {
            int index = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (index + 3 >= data.Length) break;

                    byte c = data[index];
                    byte m = data[index + 1];
                    byte yc = data[index + 2];
                    byte k = data[index + 3];

                    var (r, g, b) = TiffColorConverter.CmykToRgb(c, m, yc, k);
                    byte a = hasAlpha && index + 4 < data.Length ? data[index + 4] : (byte)255;

                    buffer.SetPixel(x, y, new Rgba32(r, g, b, a));
                    index += samplesPerPixel;
                }
            }
        }
        else if (bitsPerSample == 16)
        {
            int index = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (index + 7 >= data.Length) break;

                    ushort c16 = (ushort)(data[index] | (data[index + 1] << 8));
                    ushort m16 = (ushort)(data[index + 2] | (data[index + 3] << 8));
                    ushort y16 = (ushort)(data[index + 4] | (data[index + 5] << 8));
                    ushort k16 = (ushort)(data[index + 6] | (data[index + 7] << 8));

                    var (r, g, b) = TiffColorConverter.CmykToRgb(c16, m16, y16, k16);
                    byte a = 255;

                    if (hasAlpha && index + 9 < data.Length)
                    {
                        ushort a16 = (ushort)(data[index + 8] | (data[index + 9] << 8));
                        a = (byte)(a16 >> 8);
                    }

                    buffer.SetPixel(x, y, new Rgba32(r, g, b, a));
                    index += samplesPerPixel * 2;
                }
            }
        }
    }

    private void ConvertYCbCr(byte[] data, PixelBuffer buffer, TiffImageInfo info)
    {
        int width = info.Width;
        int height = info.Height;
        int bitsPerSample = info.BitsPerSample;
        int samplesPerPixel = info.SamplesPerPixel;
        bool hasAlpha = samplesPerPixel >= 4;

        // Get subsampling factors (default is no subsampling)
        int horizSubsampling = info.YCbCrSubSampling?.Length >= 1 ? info.YCbCrSubSampling[0] : 1;
        int vertSubsampling = info.YCbCrSubSampling?.Length >= 2 ? info.YCbCrSubSampling[1] : 1;

        // Get YCbCr coefficients (default to ITU-R BT.601)
        float lumaRed = info.YCbCrCoefficients?.Length >= 1 ? info.YCbCrCoefficients[0] : 0.299f;
        float lumaGreen = info.YCbCrCoefficients?.Length >= 2 ? info.YCbCrCoefficients[1] : 0.587f;
        float lumaBlue = info.YCbCrCoefficients?.Length >= 3 ? info.YCbCrCoefficients[2] : 0.114f;

        if (bitsPerSample == 8)
        {
            if (horizSubsampling > 1 || vertSubsampling > 1)
            {
                // Handle subsampled YCbCr (common in JPEG-in-TIFF)
                ConvertYCbCrSubsampled(data, buffer, info, horizSubsampling, vertSubsampling, 
                                       lumaRed, lumaGreen, lumaBlue);
            }
            else
            {
                // Non-subsampled YCbCr
                int index = 0;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        if (index + 2 >= data.Length) break;

                        byte yVal = data[index];
                        byte cb = data[index + 1];
                        byte cr = data[index + 2];

                        var (r, g, b) = TiffColorConverter.YCbCrToRgb(yVal, cb, cr, lumaRed, lumaGreen, lumaBlue);
                        byte a = hasAlpha && index + 3 < data.Length ? data[index + 3] : (byte)255;

                        buffer.SetPixel(x, y, new Rgba32(r, g, b, a));
                        index += samplesPerPixel;
                    }
                }
            }
        }
    }

    private void ConvertYCbCrSubsampled(byte[] data, PixelBuffer buffer, TiffImageInfo info,
        int horizSubsampling, int vertSubsampling, float lumaRed, float lumaGreen, float lumaBlue)
    {
        int width = info.Width;
        int height = info.Height;

        // MCU (Minimum Coded Unit) dimensions
        int mcuWidth = horizSubsampling;
        int mcuHeight = vertSubsampling;
        int mcusAcross = (width + mcuWidth - 1) / mcuWidth;
        int mcusDown = (height + mcuHeight - 1) / mcuHeight;

        // Each MCU contains: horizSubsampling * vertSubsampling Y samples + 1 Cb + 1 Cr
        int yCount = mcuWidth * mcuHeight;
        int dataIndex = 0;

        for (int mcuY = 0; mcuY < mcusDown; mcuY++)
        {
            for (int mcuX = 0; mcuX < mcusAcross; mcuX++)
            {
                // Read Y samples for this MCU
                var yValues = new byte[yCount];
                for (int i = 0; i < yCount && dataIndex < data.Length; i++)
                {
                    yValues[i] = data[dataIndex++];
                }

                // Read Cb and Cr (shared for entire MCU)
                byte cb = dataIndex < data.Length ? data[dataIndex++] : (byte)128;
                byte cr = dataIndex < data.Length ? data[dataIndex++] : (byte)128;

                // Convert and place pixels
                for (int dy = 0; dy < mcuHeight; dy++)
                {
                    for (int dx = 0; dx < mcuWidth; dx++)
                    {
                        int px = mcuX * mcuWidth + dx;
                        int py = mcuY * mcuHeight + dy;

                        if (px >= width || py >= height) continue;

                        int yIdx = dy * mcuWidth + dx;
                        byte yVal = yIdx < yValues.Length ? yValues[yIdx] : (byte)0;
                        var (r, g, b) = TiffColorConverter.YCbCrToRgb(yVal, cb, cr, lumaRed, lumaGreen, lumaBlue);

                        buffer.SetPixel(px, py, new Rgba32(r, g, b, 255));
                    }
                }
            }
        }
    }

    private void ConvertPalette(byte[] data, PixelBuffer buffer, TiffImageInfo info)
    {
        if (info.ColorMap == null || info.ColorMap.Length == 0)
            throw new TiffFormatException("Palette image missing color map.");

        int width = info.Width;
        int height = info.Height;
        int bitsPerSample = info.BitsPerSample;
        int colorCount = 1 << bitsPerSample;

        // Color map is arranged as R[0]..R[n-1], G[0]..G[n-1], B[0]..B[n-1]
        // where each value is 16-bit

        if (bitsPerSample == 8)
        {
            int index = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (index >= data.Length) break;

                    int paletteIndex = data[index++];
                    if (paletteIndex >= colorCount) paletteIndex = 0;

                    // 16-bit values, take high byte
                    byte r = (byte)(info.ColorMap[paletteIndex] >> 8);
                    byte g = (byte)(info.ColorMap[colorCount + paletteIndex] >> 8);
                    byte b = (byte)(info.ColorMap[2 * colorCount + paletteIndex] >> 8);

                    buffer.SetPixel(x, y, new Rgba32(r, g, b, 255));
                }
            }
        }
        else if (bitsPerSample == 4)
        {
            int byteIndex = 0;
            bool highNibble = true;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (byteIndex >= data.Length) break;

                    int paletteIndex;
                    if (highNibble)
                    {
                        paletteIndex = (data[byteIndex] >> 4) & 0x0F;
                    }
                    else
                    {
                        paletteIndex = data[byteIndex] & 0x0F;
                        byteIndex++;
                    }
                    highNibble = !highNibble;

                    byte r = (byte)(info.ColorMap[paletteIndex] >> 8);
                    byte g = (byte)(info.ColorMap[colorCount + paletteIndex] >> 8);
                    byte b = (byte)(info.ColorMap[2 * colorCount + paletteIndex] >> 8);

                    buffer.SetPixel(x, y, new Rgba32(r, g, b, 255));
                }

                // Row padding to byte boundary
                if (!highNibble)
                {
                    byteIndex++;
                    highNibble = true;
                }
            }
        }
        else if (bitsPerSample == 1)
        {
            int byteIndex = 0;
            int bitIndex = 0;

            for (int y = 0; y < height; y++)
            {
                bitIndex = 0;
                byteIndex = y * ((width + 7) / 8);

                for (int x = 0; x < width; x++)
                {
                    if (byteIndex >= data.Length) break;

                    int paletteIndex = (data[byteIndex] >> (7 - bitIndex)) & 1;

                    byte r = (byte)(info.ColorMap[paletteIndex] >> 8);
                    byte g = (byte)(info.ColorMap[colorCount + paletteIndex] >> 8);
                    byte b = (byte)(info.ColorMap[2 * colorCount + paletteIndex] >> 8);

                    buffer.SetPixel(x, y, new Rgba32(r, g, b, 255));

                    bitIndex++;
                    if (bitIndex >= 8)
                    {
                        bitIndex = 0;
                        byteIndex++;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Reads exactly count bytes from stream, handling partial reads.
    /// </summary>
    private static int ReadFully(Stream stream, byte[] buffer, int offset, int count)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = stream.Read(buffer, offset + totalRead, count - totalRead);
            if (read == 0)
                break;
            totalRead += read;
        }
        return totalRead;
    }
}

/// <summary>
/// Helper methods for predictor application.
/// </summary>
internal static class TiffPredictorHelper
{
    /// <summary>
    /// Applies horizontal differencing predictor reversal.
    /// Based on image-tiff decoder/mod.rs rev_hpredict_nsamp.
    /// </summary>
    public static void ApplyHorizontalPredictor(byte[] data, TiffImageInfo info)
    {
        int width = info.Width;
        int height = info.Height;
        int samplesPerPixel = info.SamplesPerPixel;
        int bitsPerSample = info.BitsPerSample;

        if (bitsPerSample == 8)
        {
            int bytesPerRow = width * samplesPerPixel;

            for (int y = 0; y < height; y++)
            {
                int rowStart = y * bytesPerRow;
                
                unchecked
                {
                    for (int i = samplesPerPixel; i < bytesPerRow; i++)
                    {
                        int idx = rowStart + i;
                        if (idx >= data.Length) break;
                        data[idx] = (byte)(data[idx] + data[idx - samplesPerPixel]);
                    }
                }
            }
        }
        else if (bitsPerSample == 16)
        {
            int bytesPerRow = width * samplesPerPixel * 2;
            int samplesOffset = samplesPerPixel * 2;

            for (int y = 0; y < height; y++)
            {
                int rowStart = y * bytesPerRow;
                
                unchecked
                {
                    for (int i = samplesOffset; i < bytesPerRow; i += 2)
                    {
                        int idx = rowStart + i;
                        if (idx + 1 >= data.Length) break;

                        ushort current = (ushort)(data[idx] | (data[idx + 1] << 8));
                        ushort prev = (ushort)(data[idx - samplesOffset] | (data[idx - samplesOffset + 1] << 8));
                        ushort result = (ushort)(current + prev);

                        data[idx] = (byte)(result & 0xFF);
                        data[idx + 1] = (byte)(result >> 8);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Applies floating-point predictor reversal.
    /// Based on image-tiff decoder/mod.rs predict_f16/f32/f64.
    /// The floating-point predictor first applies horizontal differencing on bytes,
    /// then reorders the shuffled bytes back to proper float representation.
    /// </summary>
    public static byte[] ApplyFloatingPointPredictor(byte[] data, TiffImageInfo info)
    {
        int width = info.Width;
        int height = info.Height;
        int samplesPerPixel = info.SamplesPerPixel;
        int bitsPerSample = info.BitsPerSample;
        int bytesPerSample = bitsPerSample / 8;
        int bytesPerRow = width * samplesPerPixel * bytesPerSample;

        var output = new byte[data.Length];

        for (int y = 0; y < height; y++)
        {
            int rowStart = y * bytesPerRow;
            int rowEnd = Math.Min(rowStart + bytesPerRow, data.Length);
            int rowLength = rowEnd - rowStart;

            if (rowLength <= 0) continue;

            // Create a working copy of this row for the horizontal differencing step
            var rowData = new byte[rowLength];
            Buffer.BlockCopy(data, rowStart, rowData, 0, rowLength);

            // Step 1: Apply horizontal differencing on byte level
            unchecked
            {
                for (int i = samplesPerPixel; i < rowLength; i++)
                {
                    rowData[i] = (byte)(rowData[i] + rowData[i - samplesPerPixel]);
                }
            }

            // Step 2: Reorder bytes from shuffled format to proper float representation
            // The encoder shuffles bytes so all MSBs are first, then all next bytes, etc.
            // We need to unshuffle them back
            int pixelCount = width * samplesPerPixel;

            if (bitsPerSample == 16 && rowLength >= pixelCount * 2)
            {
                // For f16: bytes at [i] and [i + len/2] form one half-float
                int halfLen = rowLength / 2;
                for (int i = 0; i < pixelCount; i++)
                {
                    int outIdx = rowStart + i * 2;
                    if (outIdx + 1 < output.Length && i < halfLen && i + halfLen < rowLength)
                    {
                        // Big-endian to native: reconstruct from shuffled bytes
                        output[outIdx] = rowData[i + halfLen];     // Low byte
                        output[outIdx + 1] = rowData[i];           // High byte
                    }
                }
            }
            else if (bitsPerSample == 32 && rowLength >= pixelCount * 4)
            {
                // For f32: bytes at [i, i+len/4, i+len/2, i+3*len/4] form one float
                int quarterLen = rowLength / 4;
                for (int i = 0; i < pixelCount; i++)
                {
                    int outIdx = rowStart + i * 4;
                    if (outIdx + 3 < output.Length && 
                        i < quarterLen && 
                        i + quarterLen < rowLength &&
                        i + 2 * quarterLen < rowLength &&
                        i + 3 * quarterLen < rowLength)
                    {
                        // Big-endian to native (little-endian assumed): reconstruct from shuffled bytes
                        output[outIdx] = rowData[i + 3 * quarterLen];     // Byte 0 (LSB)
                        output[outIdx + 1] = rowData[i + 2 * quarterLen]; // Byte 1
                        output[outIdx + 2] = rowData[i + quarterLen];     // Byte 2
                        output[outIdx + 3] = rowData[i];                  // Byte 3 (MSB)
                    }
                }
            }
            else if (bitsPerSample == 64 && rowLength >= pixelCount * 8)
            {
                // For f64: bytes at [i, i+len/8, i+2*len/8, ..., i+7*len/8] form one double
                int eighthLen = rowLength / 8;
                for (int i = 0; i < pixelCount; i++)
                {
                    int outIdx = rowStart + i * 8;
                    if (outIdx + 7 < output.Length && i + 7 * eighthLen < rowLength)
                    {
                        // Big-endian to native (little-endian assumed): reconstruct from shuffled bytes
                        output[outIdx] = rowData[i + 7 * eighthLen];     // Byte 0 (LSB)
                        output[outIdx + 1] = rowData[i + 6 * eighthLen]; // Byte 1
                        output[outIdx + 2] = rowData[i + 5 * eighthLen]; // Byte 2
                        output[outIdx + 3] = rowData[i + 4 * eighthLen]; // Byte 3
                        output[outIdx + 4] = rowData[i + 3 * eighthLen]; // Byte 4
                        output[outIdx + 5] = rowData[i + 2 * eighthLen]; // Byte 5
                        output[outIdx + 6] = rowData[i + eighthLen];     // Byte 6
                        output[outIdx + 7] = rowData[i];                 // Byte 7 (MSB)
                    }
                }
            }
            else
            {
                // Unsupported bit depth for floating-point predictor, just copy data
                Buffer.BlockCopy(rowData, 0, output, rowStart, rowLength);
            }
        }

        return output;
    }
}
