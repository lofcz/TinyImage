using System;
using System.IO;

namespace TinyImage.Codecs.Tga;

/// <summary>
/// TGA codec for encoding and decoding TGA (Truevision Graphics Adapter) images.
/// Supports all standard image types (uncompressed and RLE-compressed),
/// bit depths (8, 16, 24, 32), color-mapped images, and all screen origins.
/// </summary>
internal static class TgaCodec
{
    /// <summary>
    /// Decodes a TGA image from a stream.
    /// </summary>
    /// <param name="stream">The stream containing TGA data.</param>
    /// <returns>The decoded image.</returns>
    /// <exception cref="ArgumentNullException">Stream is null.</exception>
    /// <exception cref="InvalidOperationException">Invalid TGA data.</exception>
    /// <exception cref="NotSupportedException">Unsupported TGA format.</exception>
    public static Image Decode(Stream stream)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        var decoder = new TgaDecoder(stream);
        var (width, height, pixels, hasAlpha) = decoder.Decode();

        var buffer = new PixelBuffer(width, height, pixels);
        return new Image(buffer, hasAlpha);
    }

    /// <summary>
    /// Encodes an image to TGA format using default settings.
    /// Uses 32-bit if the image has alpha, otherwise 24-bit.
    /// Writes uncompressed data for maximum compatibility.
    /// </summary>
    /// <param name="image">The image to encode.</param>
    /// <param name="stream">The stream to write to.</param>
    /// <exception cref="ArgumentNullException">Image or stream is null.</exception>
    public static void Encode(Image image, Stream stream)
    {
        if (image == null)
            throw new ArgumentNullException(nameof(image));
        var bitsPerPixel = image.HasAlpha ? TgaBitsPerPixel.Bit32 : TgaBitsPerPixel.Bit24;
        Encode(image, stream, bitsPerPixel, useRle: false);
    }

    /// <summary>
    /// Encodes an image to TGA format with specified bit depth.
    /// </summary>
    /// <param name="image">The image to encode.</param>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="bitsPerPixel">The bit depth for the output (24 or 32).</param>
    /// <exception cref="ArgumentNullException">Image or stream is null.</exception>
    public static void Encode(Image image, Stream stream, TgaBitsPerPixel bitsPerPixel)
    {
        Encode(image, stream, bitsPerPixel, useRle: false);
    }

    /// <summary>
    /// Encodes an image to TGA format with specified options.
    /// </summary>
    /// <param name="image">The image to encode.</param>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="bitsPerPixel">The bit depth for the output (24 or 32).</param>
    /// <param name="useRle">Whether to use RLE compression.</param>
    /// <exception cref="ArgumentNullException">Image or stream is null.</exception>
    public static void Encode(Image image, Stream stream, TgaBitsPerPixel bitsPerPixel, bool useRle)
    {
        if (image == null)
            throw new ArgumentNullException(nameof(image));
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        var buffer = image.GetBuffer();
        var encoder = new TgaEncoder(
            stream,
            buffer.Width,
            buffer.Height,
            buffer.GetRawData(),
            bitsPerPixel,
            useRle,
            image.HasAlpha
        );

        encoder.Encode();
    }
}
