using System;
using System.IO;

namespace TinyImage.Codecs.Qoi;

/// <summary>
/// QOI codec for encoding and decoding QOI (Quite OK Image) format images.
/// </summary>
/// <remarks>
/// QOI is a fast, lossless image format that provides:
/// - 20-50x faster encoding than PNG
/// - 3-4x faster decoding than PNG
/// - Comparable compression to PNG
/// - Simple implementation (no dependencies)
/// 
/// Based on QoiSharp by Eugene Antonov (MIT License) and the QOI specification
/// by Dominic Szablewski.
/// </remarks>
internal static class QoiCodec
{
    /// <summary>
    /// Decodes a QOI image from a stream.
    /// </summary>
    /// <param name="stream">The stream containing QOI data.</param>
    /// <returns>The decoded image.</returns>
    /// <exception cref="ArgumentNullException">Stream is null.</exception>
    /// <exception cref="QoiDecodingException">Invalid QOI data.</exception>
    public static Image Decode(Stream stream)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        var decoder = new QoiDecoder(stream);
        var (width, height, pixels, hasAlpha) = decoder.Decode();

        var buffer = new PixelBuffer(width, height, pixels);
        return new Image(buffer, hasAlpha);
    }

    /// <summary>
    /// Encodes an image to QOI format using default settings (sRGB color space).
    /// </summary>
    /// <param name="image">The image to encode.</param>
    /// <param name="stream">The stream to write to.</param>
    /// <exception cref="ArgumentNullException">Image or stream is null.</exception>
    /// <exception cref="QoiEncodingException">Encoding failed.</exception>
    public static void Encode(Image image, Stream stream)
    {
        Encode(image, stream, QoiColorSpace.SRgb);
    }

    /// <summary>
    /// Encodes an image to QOI format with the specified color space.
    /// </summary>
    /// <param name="image">The image to encode.</param>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="colorSpace">The color space to encode in the header.</param>
    /// <exception cref="ArgumentNullException">Image or stream is null.</exception>
    /// <exception cref="QoiEncodingException">Encoding failed.</exception>
    public static void Encode(Image image, Stream stream, QoiColorSpace colorSpace)
    {
        if (image == null)
            throw new ArgumentNullException(nameof(image));
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        var buffer = image.GetBuffer();
        var encoder = new QoiEncoder(
            stream,
            buffer.Width,
            buffer.Height,
            buffer.GetRawData(),
            image.HasAlpha,
            colorSpace
        );

        encoder.Encode();
    }

    /// <summary>
    /// Checks if the data appears to be a valid QOI image by checking the magic bytes.
    /// </summary>
    /// <param name="data">The data to check (must be at least 4 bytes).</param>
    /// <returns>True if the data starts with QOI magic bytes "qoif".</returns>
    public static bool IsQoi(ReadOnlySpan<byte> data)
    {
        return data.Length >= 4 &&
               data[0] == 'q' && data[1] == 'o' && data[2] == 'i' && data[3] == 'f';
    }

    /// <summary>
    /// Checks if the stream appears to contain QOI data by checking the magic bytes.
    /// </summary>
    /// <param name="stream">The stream to check.</param>
    /// <returns>True if the stream starts with QOI magic bytes "qoif".</returns>
    public static bool IsQoi(Stream stream)
    {
        if (stream == null || !stream.CanRead)
            return false;

        long originalPosition = stream.CanSeek ? stream.Position : 0;

        try
        {
            byte[] magic = new byte[4];
            if (stream.Read(magic, 0, 4) != 4)
                return false;

            return magic[0] == 'q' && magic[1] == 'o' && magic[2] == 'i' && magic[3] == 'f';
        }
        finally
        {
            if (stream.CanSeek)
                stream.Position = originalPosition;
        }
    }
}
