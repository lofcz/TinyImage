using System;
using System.IO;

namespace TinyImage.Codecs.Pnm;

/// <summary>
/// Netpbm codec for encoding and decoding PBM, PGM, and PPM images.
/// Supports all format variants: P1-P6 (ASCII and binary).
/// </summary>
internal static class PnmCodec
{
    /// <summary>
    /// Decodes a Netpbm image (PBM/PGM/PPM) from a stream.
    /// </summary>
    /// <param name="stream">The stream containing PNM data.</param>
    /// <returns>The decoded image.</returns>
    /// <exception cref="ArgumentNullException">Stream is null.</exception>
    /// <exception cref="InvalidOperationException">Invalid PNM data.</exception>
    /// <exception cref="NotSupportedException">Unsupported PNM format.</exception>
    public static Image Decode(Stream stream)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        var decoder = new PnmDecoder(stream);
        var (width, height, pixels, hasAlpha) = decoder.Decode();

        var buffer = new PixelBuffer(width, height, pixels);
        return new Image(buffer, hasAlpha);
    }

    /// <summary>
    /// Encodes an image to Netpbm format using the optimal format variant.
    /// Binary PBM (P4) for black/white, binary PGM (P5) for grayscale, 
    /// or binary PPM (P6) for color images.
    /// </summary>
    /// <param name="image">The image to encode.</param>
    /// <param name="stream">The stream to write to.</param>
    /// <exception cref="ArgumentNullException">Image or stream is null.</exception>
    public static void Encode(Image image, Stream stream)
    {
        if (image == null)
            throw new ArgumentNullException(nameof(image));
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        var buffer = image.GetBuffer();
        var pixels = buffer.GetRawData();
        var format = PnmEncoder.DetermineOptimalFormat(pixels, buffer.Width, buffer.Height);

        Encode(image, stream, format);
    }

    /// <summary>
    /// Encodes an image to Netpbm format with the specified format variant.
    /// </summary>
    /// <param name="image">The image to encode.</param>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="format">The PNM format variant to use (P1-P6).</param>
    /// <exception cref="ArgumentNullException">Image or stream is null.</exception>
    public static void Encode(Image image, Stream stream, PnmFormat format)
    {
        if (image == null)
            throw new ArgumentNullException(nameof(image));
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        var buffer = image.GetBuffer();
        var encoder = new PnmEncoder(
            stream,
            buffer.Width,
            buffer.Height,
            buffer.GetRawData(),
            format
        );

        encoder.Encode();
    }

    /// <summary>
    /// Encodes an image to PBM format (black and white).
    /// Uses binary format (P4) by default for smaller file size.
    /// </summary>
    /// <param name="image">The image to encode.</param>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="ascii">If true, use ASCII format (P1); otherwise binary (P4).</param>
    public static void EncodePbm(Image image, Stream stream, bool ascii = false)
    {
        Encode(image, stream, ascii ? PnmFormat.P1 : PnmFormat.P4);
    }

    /// <summary>
    /// Encodes an image to PGM format (grayscale).
    /// Uses binary format (P5) by default for smaller file size.
    /// </summary>
    /// <param name="image">The image to encode.</param>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="ascii">If true, use ASCII format (P2); otherwise binary (P5).</param>
    public static void EncodePgm(Image image, Stream stream, bool ascii = false)
    {
        Encode(image, stream, ascii ? PnmFormat.P2 : PnmFormat.P5);
    }

    /// <summary>
    /// Encodes an image to PPM format (RGB color).
    /// Uses binary format (P6) by default for smaller file size.
    /// </summary>
    /// <param name="image">The image to encode.</param>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="ascii">If true, use ASCII format (P3); otherwise binary (P6).</param>
    public static void EncodePpm(Image image, Stream stream, bool ascii = false)
    {
        Encode(image, stream, ascii ? PnmFormat.P3 : PnmFormat.P6);
    }
}
