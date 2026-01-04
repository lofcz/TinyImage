using System;
using System.Buffers;
using System.IO;

namespace TinyImage.Codecs.Jpeg;

/// <summary>
/// JPEG codec for encoding and decoding JPEG images.
/// Based on JpegLibrary (MIT).
/// </summary>
internal static class JpegCodec
{
    /// <summary>
    /// Decodes a JPEG image from a stream.
    /// </summary>
    /// <param name="stream">The stream containing JPEG data.</param>
    /// <returns>The decoded image.</returns>
    public static Image Decode(Stream stream)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        // Read stream into memory
        byte[] data;
        if (stream is MemoryStream ms && ms.TryGetBuffer(out var segment))
        {
            data = segment.Array!;
            if (segment.Offset != 0 || segment.Count != data.Length)
            {
                data = new byte[segment.Count];
                Array.Copy(segment.Array!, segment.Offset, data, 0, segment.Count);
            }
        }
        else
        {
            using var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            data = memoryStream.ToArray();
        }

        // Create decoder
        var decoder = new JpegDecoder();
        decoder.SetInput(data);

        // Identify the image (read headers)
        decoder.Identify();

        // Get dimensions
        int width = decoder.Width;
        int height = decoder.Height;
        int componentCount = decoder.NumberOfComponents;

        if (width <= 0 || height <= 0)
            throw new InvalidOperationException("Invalid JPEG dimensions.");

        // Create output writer
        var outputWriter = new JpegRgbOutputWriter(width, height, componentCount);

        // Set up decoder
        decoder.SetOutputWriter(outputWriter);

        // Decode
        decoder.Decode();

        // Convert YCbCr to RGB if needed
        outputWriter.ConvertYCbCrToRgb();

        // Create pixel buffer
        var buffer = new PixelBuffer(width, height, outputWriter.GetBuffer());

        return new Image(buffer, hasAlpha: false);
    }

    /// <summary>
    /// Encodes an image to JPEG format.
    /// </summary>
    /// <param name="image">The image to encode.</param>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="quality">The JPEG quality (1-100). Default is 90.</param>
    public static void Encode(Image image, Stream stream, int quality = 90)
    {
        if (image == null)
            throw new ArgumentNullException(nameof(image));
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));
        if (quality < 1 || quality > 100)
            throw new ArgumentOutOfRangeException(nameof(quality), "Quality must be between 1 and 100.");

        int width = image.Width;
        int height = image.Height;

        // Get raw RGBA data
        var buffer = image.GetBuffer();
        var rgbaData = buffer.GetRawData();

        // Create input reader
        var inputReader = new JpegRgbInputReader(rgbaData, width, height);

        // Create encoder
        var encoder = new JpegEncoder();
        encoder.SetQuantizationTable(JpegStandardQuantizationTable.ScaleByQuality(
            JpegStandardQuantizationTable.GetLuminanceTable(JpegElementPrecision.Precision8Bit, 0),
            quality));
        encoder.SetQuantizationTable(JpegStandardQuantizationTable.ScaleByQuality(
            JpegStandardQuantizationTable.GetChrominanceTable(JpegElementPrecision.Precision8Bit, 1),
            quality));

        encoder.SetHuffmanTable(true, 0, JpegStandardHuffmanEncodingTable.GetLuminanceDCTable());
        encoder.SetHuffmanTable(false, 0, JpegStandardHuffmanEncodingTable.GetLuminanceACTable());
        encoder.SetHuffmanTable(true, 1, JpegStandardHuffmanEncodingTable.GetChrominanceDCTable());
        encoder.SetHuffmanTable(false, 1, JpegStandardHuffmanEncodingTable.GetChrominanceACTable());

        // Add components with 4:4:4 subsampling (no subsampling - best quality)
        encoder.AddComponent(1, 0, 0, 0, 1, 1); // Y
        encoder.AddComponent(2, 1, 1, 1, 1, 1); // Cb
        encoder.AddComponent(3, 1, 1, 1, 1, 1); // Cr

        encoder.SetInputReader(inputReader);

        // Create output buffer
        var outputBuffer = new ArrayBufferWriter<byte>();
        encoder.SetOutput(outputBuffer);
        
        // Encode
        encoder.Encode();

        // Write to stream
        stream.Write(outputBuffer.WrittenSpan.ToArray(), 0, outputBuffer.WrittenCount);
    }
}

/// <summary>
/// Simple IBufferWriter implementation using an array.
/// </summary>
internal sealed class ArrayBufferWriter<T> : IBufferWriter<T>
{
    private T[] _buffer;
    private int _index;

    private const int DefaultInitialBufferSize = 256;

    public ArrayBufferWriter()
    {
        _buffer = Array.Empty<T>();
        _index = 0;
    }

    public ReadOnlyMemory<T> WrittenMemory => _buffer.AsMemory(0, _index);
    public ReadOnlySpan<T> WrittenSpan => _buffer.AsSpan(0, _index);
    public int WrittenCount => _index;
    public int FreeCapacity => _buffer.Length - _index;

    public void Clear()
    {
        _buffer.AsSpan(0, _index).Clear();
        _index = 0;
    }

    public void Advance(int count)
    {
        if (count < 0)
            throw new ArgumentException("Count cannot be negative.", nameof(count));

        if (_index > _buffer.Length - count)
            throw new InvalidOperationException("Cannot advance past the end of the buffer.");

        _index += count;
    }

    public Memory<T> GetMemory(int sizeHint = 0)
    {
        CheckAndResizeBuffer(sizeHint);
        return _buffer.AsMemory(_index);
    }

    public Span<T> GetSpan(int sizeHint = 0)
    {
        CheckAndResizeBuffer(sizeHint);
        return _buffer.AsSpan(_index);
    }

    private void CheckAndResizeBuffer(int sizeHint)
    {
        if (sizeHint < 0)
            throw new ArgumentException("Size hint cannot be negative.", nameof(sizeHint));

        if (sizeHint == 0)
            sizeHint = 1;

        if (sizeHint > FreeCapacity)
        {
            int currentLength = _buffer.Length;
            int growBy = Math.Max(sizeHint, currentLength);

            if (currentLength == 0)
                growBy = Math.Max(growBy, DefaultInitialBufferSize);

            int newSize = currentLength + growBy;

            Array.Resize(ref _buffer, newSize);
        }
    }
}
