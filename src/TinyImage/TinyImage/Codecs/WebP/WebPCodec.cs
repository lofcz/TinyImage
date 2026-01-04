using System;
using System.IO;

namespace TinyImage.Codecs.WebP;

/// <summary>
/// WebP codec for encoding and decoding WebP images.
/// Supports lossy (VP8), lossless (VP8L), animated, and alpha formats.
/// </summary>
internal static class WebPCodec
{
    /// <summary>
    /// Decodes a WebP image from a stream.
    /// </summary>
    /// <param name="stream">The stream containing WebP data.</param>
    /// <returns>The decoded image with all frames.</returns>
    public static Image Decode(Stream stream)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        var decoder = new WebPDecoder(stream);
        return decoder.Decode();
    }

    /// <summary>
    /// Encodes an image to WebP format.
    /// </summary>
    /// <param name="image">The image to encode.</param>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="lossless">If true, use lossless encoding; otherwise use lossy encoding.</param>
    /// <param name="quality">Quality factor for lossy encoding (0-100).</param>
    public static void Encode(Image image, Stream stream, bool lossless = false, int quality = 80)
    {
        if (image == null)
            throw new ArgumentNullException(nameof(image));
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        var options = new WebPEncoder.EncoderOptions
        {
            Lossless = lossless,
            Quality = quality,
            LoopCount = image.LoopCount
        };

        var encoder = new WebPEncoder(stream, options);
        encoder.Encode(image);
    }

    /// <summary>
    /// Checks if the stream contains a valid WebP signature.
    /// </summary>
    /// <param name="stream">The stream to check.</param>
    /// <returns>True if the stream appears to contain WebP data.</returns>
    public static bool IsWebP(Stream stream)
    {
        if (stream == null || !stream.CanRead)
            return false;

        long originalPosition = stream.CanSeek ? stream.Position : 0;

        try
        {
            byte[] header = new byte[12];
            if (stream.Read(header, 0, 12) != 12)
                return false;

            // Check for RIFF header and WEBP signature
            return header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F' &&
                   header[8] == 'W' && header[9] == 'E' && header[10] == 'B' && header[11] == 'P';
        }
        finally
        {
            if (stream.CanSeek)
                stream.Position = originalPosition;
        }
    }
}
