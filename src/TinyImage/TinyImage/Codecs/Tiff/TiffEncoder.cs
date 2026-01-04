using System;
using System.Collections.Generic;
using System.IO;

namespace TinyImage.Codecs.Tiff;

/// <summary>
/// Encodes images to TIFF format.
/// Based on image-tiff encoder/mod.rs.
/// </summary>
internal sealed class TiffEncoder
{
    private readonly Stream _stream;
    private readonly TiffByteOrder _byteOrder;
    private readonly TiffCompression _compression;
    private readonly TiffPredictor _predictor;
    private bool _useBigTiff;
    private bool _forceBigTiff;

    /// <summary>
    /// Threshold size (in bytes) above which BigTIFF format is used automatically.
    /// Default is 4GB - some headroom.
    /// </summary>
    private const long BigTiffThreshold = 0xFFFF_FFF0; // ~4GB

    /// <summary>
    /// Creates a new TIFF encoder.
    /// </summary>
    public TiffEncoder(Stream stream, TiffCompression compression = TiffCompression.None, 
                       TiffPredictor predictor = TiffPredictor.None, bool forceBigTiff = false)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _byteOrder = new TiffByteOrder(true); // Use little-endian
        _compression = compression;
        _predictor = predictor;
        _forceBigTiff = forceBigTiff;
        _useBigTiff = forceBigTiff;
    }

    /// <summary>
    /// Encodes an image to the stream.
    /// </summary>
    public void Encode(Image image)
    {
        if (image == null)
            throw new ArgumentNullException(nameof(image));

        // Check if we need BigTIFF based on estimated size
        if (!_forceBigTiff)
        {
            long estimatedSize = EstimateImageSize(image);
            _useBigTiff = estimatedSize > BigTiffThreshold;
        }

        WriteHeader();
        WriteImage(image);
    }

    /// <summary>
    /// Encodes multiple images (multi-page TIFF).
    /// </summary>
    public void Encode(IList<Image> images)
    {
        if (images == null || images.Count == 0)
            throw new ArgumentException("At least one image is required.", nameof(images));

        // Check if we need BigTIFF based on estimated total size
        if (!_forceBigTiff)
        {
            long estimatedSize = 0;
            foreach (var image in images)
            {
                estimatedSize += EstimateImageSize(image);
            }
            _useBigTiff = estimatedSize > BigTiffThreshold;
        }

        WriteHeader();

        for (int i = 0; i < images.Count; i++)
        {
            WriteImage(images[i]);
        }
    }

    /// <summary>
    /// Estimates the uncompressed size of an image in bytes.
    /// </summary>
    private static long EstimateImageSize(Image image)
    {
        // Rough estimate: width * height * 4 bytes per pixel + IFD overhead
        return (long)image.Width * image.Height * 4 + 4096;
    }

    private void WriteHeader()
    {
        // Byte order marker: "II" for little-endian
        _stream.WriteByte(0x49);
        _stream.WriteByte(0x49);

        if (_useBigTiff)
        {
            // BigTIFF magic number: 43
            _byteOrder.WriteUInt16(_stream, TiffConstants.BigTiffMagic);
            
            // Offset byte size: 8
            _byteOrder.WriteUInt16(_stream, 8);
            
            // Reserved: 0
            _byteOrder.WriteUInt16(_stream, 0);
            
            // First IFD offset (will be right after header = 16 bytes)
            _byteOrder.WriteUInt64(_stream, 16);
        }
        else
        {
            // Standard TIFF magic number: 42
            _byteOrder.WriteUInt16(_stream, TiffConstants.TiffMagic);

            // First IFD offset (will be right after header = 8 bytes)
            _byteOrder.WriteUInt32(_stream, 8);
        }
    }

    private void WriteImage(Image image)
    {
        if (_useBigTiff)
        {
            WriteImageBigTiff(image);
        }
        else
        {
            WriteImageStandard(image);
        }
    }

    private void WriteImageStandard(Image image)
    {
        int width = image.Width;
        int height = image.Height;
        bool hasAlpha = image.HasAlpha;
        int samplesPerPixel = hasAlpha ? 4 : 3;
        int bitsPerSample = 8;

        // Calculate strip parameters
        int rowBytes = width * samplesPerPixel;
        int rowsPerStrip = Math.Max(1, TiffConstants.DefaultStripSizeTarget / rowBytes);
        if (rowsPerStrip > height) rowsPerStrip = height;

        int stripCount = (height + rowsPerStrip - 1) / rowsPerStrip;

        // Convert image to raw bytes
        byte[] rawData = ConvertToRawBytes(image, samplesPerPixel);

        // Apply predictor if needed
        if (_predictor == TiffPredictor.Horizontal)
        {
            ApplyHorizontalPredictor(rawData, width, height, samplesPerPixel);
        }

        // Compress strips
        var compressedStrips = new byte[stripCount][];
        var stripByteCounts = new int[stripCount];

        for (int i = 0; i < stripCount; i++)
        {
            int startRow = i * rowsPerStrip;
            int rowsInStrip = Math.Min(rowsPerStrip, height - startRow);
            int stripSize = rowsInStrip * rowBytes;

            var stripData = new byte[stripSize];
            Buffer.BlockCopy(rawData, startRow * rowBytes, stripData, 0, stripSize);

            compressedStrips[i] = CompressStrip(stripData);
            stripByteCounts[i] = compressedStrips[i].Length;
        }

        // Build IFD entries list - will be written in tag order
        var ifdEntries = new SortedList<ushort, IfdEntryData>();

        // Required tags
        ifdEntries.Add((ushort)TiffTag.ImageWidth, new IfdEntryData(TiffTag.ImageWidth, TiffFieldType.Long, 1, BitConverter.GetBytes((uint)width)));
        ifdEntries.Add((ushort)TiffTag.ImageLength, new IfdEntryData(TiffTag.ImageLength, TiffFieldType.Long, 1, BitConverter.GetBytes((uint)height)));
        
        // BitsPerSample - array of shorts
        var bpsData = new byte[samplesPerPixel * 2];
        for (int i = 0; i < samplesPerPixel; i++)
        {
            bpsData[i * 2] = (byte)bitsPerSample;
            bpsData[i * 2 + 1] = 0;
        }
        ifdEntries.Add((ushort)TiffTag.BitsPerSample, new IfdEntryData(TiffTag.BitsPerSample, TiffFieldType.Short, (uint)samplesPerPixel, bpsData));

        ifdEntries.Add((ushort)TiffTag.Compression, new IfdEntryData(TiffTag.Compression, TiffFieldType.Short, 1, BitConverter.GetBytes((ushort)_compression)));
        ifdEntries.Add((ushort)TiffTag.PhotometricInterpretation, new IfdEntryData(TiffTag.PhotometricInterpretation, TiffFieldType.Short, 1, BitConverter.GetBytes((ushort)TiffPhotometric.Rgb)));
        
        // StripOffsets - placeholder, will be calculated
        ifdEntries.Add((ushort)TiffTag.StripOffsets, new IfdEntryData(TiffTag.StripOffsets, TiffFieldType.Long, (uint)stripCount, new byte[stripCount * 4]));
        
        ifdEntries.Add((ushort)TiffTag.SamplesPerPixel, new IfdEntryData(TiffTag.SamplesPerPixel, TiffFieldType.Short, 1, BitConverter.GetBytes((ushort)samplesPerPixel)));
        ifdEntries.Add((ushort)TiffTag.RowsPerStrip, new IfdEntryData(TiffTag.RowsPerStrip, TiffFieldType.Long, 1, BitConverter.GetBytes((uint)rowsPerStrip)));
        
        // StripByteCounts
        var sbcData = new byte[stripCount * 4];
        for (int i = 0; i < stripCount; i++)
        {
            var countBytes = BitConverter.GetBytes((uint)stripByteCounts[i]);
            Buffer.BlockCopy(countBytes, 0, sbcData, i * 4, 4);
        }
        ifdEntries.Add((ushort)TiffTag.StripByteCounts, new IfdEntryData(TiffTag.StripByteCounts, TiffFieldType.Long, (uint)stripCount, sbcData));

        // XResolution and YResolution - rational values (72/1)
        var resData = new byte[8];
        BitConverter.GetBytes((uint)72).CopyTo(resData, 0);
        BitConverter.GetBytes((uint)1).CopyTo(resData, 4);
        ifdEntries.Add((ushort)TiffTag.XResolution, new IfdEntryData(TiffTag.XResolution, TiffFieldType.Rational, 1, resData));
        ifdEntries.Add((ushort)TiffTag.YResolution, new IfdEntryData(TiffTag.YResolution, TiffFieldType.Rational, 1, (byte[])resData.Clone()));

        ifdEntries.Add((ushort)TiffTag.PlanarConfiguration, new IfdEntryData(TiffTag.PlanarConfiguration, TiffFieldType.Short, 1, BitConverter.GetBytes((ushort)TiffPlanarConfig.Chunky)));
        ifdEntries.Add((ushort)TiffTag.ResolutionUnit, new IfdEntryData(TiffTag.ResolutionUnit, TiffFieldType.Short, 1, BitConverter.GetBytes((ushort)TiffResolutionUnit.Inch)));

        if (_predictor != TiffPredictor.None)
        {
            ifdEntries.Add((ushort)TiffTag.Predictor, new IfdEntryData(TiffTag.Predictor, TiffFieldType.Short, 1, BitConverter.GetBytes((ushort)_predictor)));
        }

        if (hasAlpha)
        {
            ifdEntries.Add((ushort)TiffTag.ExtraSamples, new IfdEntryData(TiffTag.ExtraSamples, TiffFieldType.Short, 1, BitConverter.GetBytes((ushort)TiffExtraSamples.UnassociatedAlpha)));
        }

        // Calculate IFD size and positions
        int entryCount = ifdEntries.Count;
        int ifdSize = 2 + entryCount * 12 + 4; // count + entries + next IFD offset

        // Calculate total extra data size (values that don't fit inline)
        int extraDataSize = 0;
        foreach (var entry in ifdEntries.Values)
        {
            if (entry.Data.Length > 4)
            {
                extraDataSize += entry.Data.Length;
            }
        }

        long ifdStart = _stream.Position;
        long extraDataStart = ifdStart + ifdSize;
        long stripDataStart = extraDataStart + extraDataSize;

        // Calculate strip offsets
        var stripOffsets = new uint[stripCount];
        long currentOffset = stripDataStart;
        for (int i = 0; i < stripCount; i++)
        {
            stripOffsets[i] = (uint)currentOffset;
            currentOffset += stripByteCounts[i];
        }

        // Update StripOffsets entry data
        var soData = new byte[stripCount * 4];
        for (int i = 0; i < stripCount; i++)
        {
            var offsetBytes = BitConverter.GetBytes(stripOffsets[i]);
            Buffer.BlockCopy(offsetBytes, 0, soData, i * 4, 4);
        }
        ifdEntries[(ushort)TiffTag.StripOffsets] = new IfdEntryData(TiffTag.StripOffsets, TiffFieldType.Long, (uint)stripCount, soData);

        // Write IFD
        _byteOrder.WriteUInt16(_stream, (ushort)entryCount);

        long currentExtraDataOffset = extraDataStart;
        foreach (var entry in ifdEntries.Values)
        {
            WriteIfdEntry(entry, ref currentExtraDataOffset);
        }

        // Write next IFD offset (0 = no more IFDs)
        _byteOrder.WriteUInt32(_stream, 0);

        // Write extra data (values that don't fit inline)
        foreach (var entry in ifdEntries.Values)
        {
            if (entry.Data.Length > 4)
            {
                _stream.Write(entry.Data, 0, entry.Data.Length);
            }
        }

        // Write strip data
        foreach (var strip in compressedStrips)
        {
            _stream.Write(strip, 0, strip.Length);
        }
    }

    private void WriteImageBigTiff(Image image)
    {
        int width = image.Width;
        int height = image.Height;
        bool hasAlpha = image.HasAlpha;
        int samplesPerPixel = hasAlpha ? 4 : 3;
        int bitsPerSample = 8;

        // Calculate strip parameters
        int rowBytes = width * samplesPerPixel;
        int rowsPerStrip = Math.Max(1, TiffConstants.DefaultStripSizeTarget / rowBytes);
        if (rowsPerStrip > height) rowsPerStrip = height;

        int stripCount = (height + rowsPerStrip - 1) / rowsPerStrip;

        // Convert image to raw bytes
        byte[] rawData = ConvertToRawBytes(image, samplesPerPixel);

        // Apply predictor if needed
        if (_predictor == TiffPredictor.Horizontal)
        {
            ApplyHorizontalPredictor(rawData, width, height, samplesPerPixel);
        }

        // Compress strips
        var compressedStrips = new byte[stripCount][];
        var stripByteCounts = new long[stripCount];

        for (int i = 0; i < stripCount; i++)
        {
            int startRow = i * rowsPerStrip;
            int rowsInStrip = Math.Min(rowsPerStrip, height - startRow);
            int stripSize = rowsInStrip * rowBytes;

            var stripData = new byte[stripSize];
            Buffer.BlockCopy(rawData, startRow * rowBytes, stripData, 0, stripSize);

            compressedStrips[i] = CompressStrip(stripData);
            stripByteCounts[i] = compressedStrips[i].Length;
        }

        // Build BigTIFF IFD entries list - will be written in tag order
        var ifdEntries = new SortedList<ushort, BigTiffIfdEntryData>();

        // Required tags (using Long8 for offsets and counts that may exceed 4GB)
        ifdEntries.Add((ushort)TiffTag.ImageWidth, new BigTiffIfdEntryData(TiffTag.ImageWidth, TiffFieldType.Long, 1, BitConverter.GetBytes((uint)width)));
        ifdEntries.Add((ushort)TiffTag.ImageLength, new BigTiffIfdEntryData(TiffTag.ImageLength, TiffFieldType.Long, 1, BitConverter.GetBytes((uint)height)));
        
        // BitsPerSample - array of shorts
        var bpsData = new byte[samplesPerPixel * 2];
        for (int i = 0; i < samplesPerPixel; i++)
        {
            bpsData[i * 2] = (byte)bitsPerSample;
            bpsData[i * 2 + 1] = 0;
        }
        ifdEntries.Add((ushort)TiffTag.BitsPerSample, new BigTiffIfdEntryData(TiffTag.BitsPerSample, TiffFieldType.Short, (ulong)samplesPerPixel, bpsData));

        ifdEntries.Add((ushort)TiffTag.Compression, new BigTiffIfdEntryData(TiffTag.Compression, TiffFieldType.Short, 1, BitConverter.GetBytes((ushort)_compression)));
        ifdEntries.Add((ushort)TiffTag.PhotometricInterpretation, new BigTiffIfdEntryData(TiffTag.PhotometricInterpretation, TiffFieldType.Short, 1, BitConverter.GetBytes((ushort)TiffPhotometric.Rgb)));
        
        // StripOffsets - using Long8 for BigTIFF
        ifdEntries.Add((ushort)TiffTag.StripOffsets, new BigTiffIfdEntryData(TiffTag.StripOffsets, TiffFieldType.Long8, (ulong)stripCount, new byte[stripCount * 8]));
        
        ifdEntries.Add((ushort)TiffTag.SamplesPerPixel, new BigTiffIfdEntryData(TiffTag.SamplesPerPixel, TiffFieldType.Short, 1, BitConverter.GetBytes((ushort)samplesPerPixel)));
        ifdEntries.Add((ushort)TiffTag.RowsPerStrip, new BigTiffIfdEntryData(TiffTag.RowsPerStrip, TiffFieldType.Long, 1, BitConverter.GetBytes((uint)rowsPerStrip)));
        
        // StripByteCounts - using Long8 for BigTIFF
        var sbcData = new byte[stripCount * 8];
        for (int i = 0; i < stripCount; i++)
        {
            var countBytes = BitConverter.GetBytes((ulong)stripByteCounts[i]);
            Buffer.BlockCopy(countBytes, 0, sbcData, i * 8, 8);
        }
        ifdEntries.Add((ushort)TiffTag.StripByteCounts, new BigTiffIfdEntryData(TiffTag.StripByteCounts, TiffFieldType.Long8, (ulong)stripCount, sbcData));

        // XResolution and YResolution - rational values (72/1)
        var resData = new byte[8];
        BitConverter.GetBytes((uint)72).CopyTo(resData, 0);
        BitConverter.GetBytes((uint)1).CopyTo(resData, 4);
        ifdEntries.Add((ushort)TiffTag.XResolution, new BigTiffIfdEntryData(TiffTag.XResolution, TiffFieldType.Rational, 1, resData));
        ifdEntries.Add((ushort)TiffTag.YResolution, new BigTiffIfdEntryData(TiffTag.YResolution, TiffFieldType.Rational, 1, (byte[])resData.Clone()));

        ifdEntries.Add((ushort)TiffTag.PlanarConfiguration, new BigTiffIfdEntryData(TiffTag.PlanarConfiguration, TiffFieldType.Short, 1, BitConverter.GetBytes((ushort)TiffPlanarConfig.Chunky)));
        ifdEntries.Add((ushort)TiffTag.ResolutionUnit, new BigTiffIfdEntryData(TiffTag.ResolutionUnit, TiffFieldType.Short, 1, BitConverter.GetBytes((ushort)TiffResolutionUnit.Inch)));

        if (_predictor != TiffPredictor.None)
        {
            ifdEntries.Add((ushort)TiffTag.Predictor, new BigTiffIfdEntryData(TiffTag.Predictor, TiffFieldType.Short, 1, BitConverter.GetBytes((ushort)_predictor)));
        }

        if (hasAlpha)
        {
            ifdEntries.Add((ushort)TiffTag.ExtraSamples, new BigTiffIfdEntryData(TiffTag.ExtraSamples, TiffFieldType.Short, 1, BitConverter.GetBytes((ushort)TiffExtraSamples.UnassociatedAlpha)));
        }

        // Calculate BigTIFF IFD size and positions
        int entryCount = ifdEntries.Count;
        // BigTIFF: 8 bytes count + entries * 20 bytes + 8 bytes next IFD offset
        int ifdSize = 8 + entryCount * TiffConstants.BigTiffIfdEntrySize + 8;

        // Calculate total extra data size (values that don't fit inline - 8 bytes for BigTIFF)
        int extraDataSize = 0;
        foreach (var entry in ifdEntries.Values)
        {
            if (entry.Data.Length > 8)
            {
                extraDataSize += entry.Data.Length;
            }
        }

        long ifdStart = _stream.Position;
        long extraDataStart = ifdStart + ifdSize;
        long stripDataStart = extraDataStart + extraDataSize;

        // Calculate strip offsets (64-bit)
        var stripOffsets = new ulong[stripCount];
        long currentOffset = stripDataStart;
        for (int i = 0; i < stripCount; i++)
        {
            stripOffsets[i] = (ulong)currentOffset;
            currentOffset += stripByteCounts[i];
        }

        // Update StripOffsets entry data
        var soData = new byte[stripCount * 8];
        for (int i = 0; i < stripCount; i++)
        {
            var offsetBytes = BitConverter.GetBytes(stripOffsets[i]);
            Buffer.BlockCopy(offsetBytes, 0, soData, i * 8, 8);
        }
        ifdEntries[(ushort)TiffTag.StripOffsets] = new BigTiffIfdEntryData(TiffTag.StripOffsets, TiffFieldType.Long8, (ulong)stripCount, soData);

        // Write BigTIFF IFD
        _byteOrder.WriteUInt64(_stream, (ulong)entryCount);

        long currentExtraDataOffset = extraDataStart;
        foreach (var entry in ifdEntries.Values)
        {
            WriteBigTiffIfdEntry(entry, ref currentExtraDataOffset);
        }

        // Write next IFD offset (0 = no more IFDs)
        _byteOrder.WriteUInt64(_stream, 0);

        // Write extra data (values that don't fit inline)
        foreach (var entry in ifdEntries.Values)
        {
            if (entry.Data.Length > 8)
            {
                _stream.Write(entry.Data, 0, entry.Data.Length);
            }
        }

        // Write strip data
        foreach (var strip in compressedStrips)
        {
            _stream.Write(strip, 0, strip.Length);
        }
    }

    private void WriteIfdEntry(IfdEntryData entry, ref long currentExtraDataOffset)
    {
        _byteOrder.WriteUInt16(_stream, (ushort)entry.Tag);
        _byteOrder.WriteUInt16(_stream, (ushort)entry.Type);
        _byteOrder.WriteUInt32(_stream, entry.Count);

        if (entry.Data.Length <= 4)
        {
            // Write inline value (padded to 4 bytes)
            _stream.Write(entry.Data, 0, entry.Data.Length);
            for (int i = entry.Data.Length; i < 4; i++)
                _stream.WriteByte(0);
        }
        else
        {
            // Write offset to extra data
            _byteOrder.WriteUInt32(_stream, (uint)currentExtraDataOffset);
            currentExtraDataOffset += entry.Data.Length;
        }
    }

    private byte[] ConvertToRawBytes(Image image, int samplesPerPixel)
    {
        int width = image.Width;
        int height = image.Height;
        var result = new byte[width * height * samplesPerPixel];
        int index = 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var pixel = image.GetPixel(x, y);
                result[index++] = pixel.R;
                result[index++] = pixel.G;
                result[index++] = pixel.B;
                if (samplesPerPixel == 4)
                {
                    result[index++] = pixel.A;
                }
            }
        }

        return result;
    }

    private void ApplyHorizontalPredictor(byte[] data, int width, int height, int samplesPerPixel)
    {
        int rowBytes = width * samplesPerPixel;

        // Process from right to left to avoid overwriting data we still need
        for (int y = 0; y < height; y++)
        {
            int rowStart = y * rowBytes;

            unchecked
            {
                for (int i = rowBytes - 1; i >= samplesPerPixel; i--)
                {
                    int idx = rowStart + i;
                    data[idx] = (byte)(data[idx] - data[idx - samplesPerPixel]);
                }
            }
        }
    }

    private byte[] CompressStrip(byte[] data)
    {
        return _compression switch
        {
            TiffCompression.None => data,
            TiffCompression.Lzw => TiffLzwEncoder.Encode(data),
            TiffCompression.Deflate or TiffCompression.OldDeflate => TiffDeflateEncoder.Encode(data),
            TiffCompression.PackBits => TiffPackBitsEncoder.Encode(data),
            _ => data
        };
    }

    private void WriteBigTiffIfdEntry(BigTiffIfdEntryData entry, ref long currentExtraDataOffset)
    {
        _byteOrder.WriteUInt16(_stream, (ushort)entry.Tag);
        _byteOrder.WriteUInt16(_stream, (ushort)entry.Type);
        _byteOrder.WriteUInt64(_stream, entry.Count);

        if (entry.Data.Length <= 8)
        {
            // Write inline value (padded to 8 bytes)
            _stream.Write(entry.Data, 0, entry.Data.Length);
            for (int i = entry.Data.Length; i < 8; i++)
                _stream.WriteByte(0);
        }
        else
        {
            // Write offset to extra data
            _byteOrder.WriteUInt64(_stream, (ulong)currentExtraDataOffset);
            currentExtraDataOffset += entry.Data.Length;
        }
    }

    private readonly struct IfdEntryData
    {
        public TiffTag Tag { get; }
        public TiffFieldType Type { get; }
        public uint Count { get; }
        public byte[] Data { get; }

        public IfdEntryData(TiffTag tag, TiffFieldType type, uint count, byte[] data)
        {
            Tag = tag;
            Type = type;
            Count = count;
            Data = data;
        }
    }

    private readonly struct BigTiffIfdEntryData
    {
        public TiffTag Tag { get; }
        public TiffFieldType Type { get; }
        public ulong Count { get; }
        public byte[] Data { get; }

        public BigTiffIfdEntryData(TiffTag tag, TiffFieldType type, ulong count, byte[] data)
        {
            Tag = tag;
            Type = type;
            Count = count;
            Data = data;
        }
    }
}
