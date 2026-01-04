using System;
using System.IO;

namespace TinyImage.Codecs.Bmp;

/// <summary>
/// BMP codec for encoding and decoding BMP (Bitmap) images.
/// Supports all standard bit depths (1, 2, 4, 8, 16, 24, 32) and
/// compression types (RGB, RLE4, RLE8, BitFields).
/// </summary>
internal static class BmpCodec
{
    /// <summary>
    /// Decodes a BMP image from a stream.
    /// </summary>
    /// <param name="stream">The stream containing BMP data.</param>
    /// <returns>The decoded image.</returns>
    /// <exception cref="ArgumentNullException">Stream is null.</exception>
    /// <exception cref="InvalidOperationException">Invalid BMP data.</exception>
    /// <exception cref="NotSupportedException">Unsupported BMP format.</exception>
    public static Image Decode(Stream stream)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        var decoder = new BmpDecoder(stream);
        var (width, height, pixels, hasAlpha) = decoder.Decode();

        var buffer = new PixelBuffer(width, height, pixels);
        return new Image(buffer, hasAlpha);
    }

    /// <summary>
    /// Encodes an image to BMP format using 24-bit color (default).
    /// </summary>
    /// <param name="image">The image to encode.</param>
    /// <param name="stream">The stream to write to.</param>
    /// <exception cref="ArgumentNullException">Image or stream is null.</exception>
    public static void Encode(Image image, Stream stream)
    {
        Encode(image, stream, image.HasAlpha ? BmpBitsPerPixel.Bit32 : BmpBitsPerPixel.Bit24);
    }

    /// <summary>
    /// Encodes an image to BMP format with the specified bits per pixel.
    /// </summary>
    /// <param name="image">The image to encode.</param>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="bitsPerPixel">The desired bits per pixel for the output.</param>
    /// <exception cref="ArgumentNullException">Image or stream is null.</exception>
    public static void Encode(Image image, Stream stream, BmpBitsPerPixel bitsPerPixel)
    {
        if (image == null)
            throw new ArgumentNullException(nameof(image));
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        var buffer = image.GetBuffer();
        var encoder = new BmpEncoder(
            stream,
            buffer.Width,
            buffer.Height,
            buffer.GetRawData(),
            bitsPerPixel,
            image.HasAlpha
        );

        encoder.Encode();
    }
}
