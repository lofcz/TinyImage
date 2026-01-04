using System;
using System.Collections.Generic;
using System.IO;

namespace TinyImage.Codecs.Tiff;

/// <summary>
/// TIFF codec for encoding and decoding TIFF images.
/// </summary>
internal static class TiffCodec
{
    /// <summary>
    /// Decodes a TIFF image from a stream.
    /// </summary>
    /// <param name="stream">The input stream.</param>
    /// <returns>The decoded image (first frame for multi-page TIFFs).</returns>
    public static Image Decode(Stream stream)
    {
        var decoder = new TiffDecoder(stream);
        return decoder.DecodeFirstImage();
    }

    /// <summary>
    /// Decodes all pages from a multi-page TIFF.
    /// </summary>
    /// <param name="stream">The input stream.</param>
    /// <returns>List of all images in the TIFF.</returns>
    public static List<Image> DecodeAll(Stream stream)
    {
        var decoder = new TiffDecoder(stream);
        return decoder.Decode();
    }

    /// <summary>
    /// Encodes an image to TIFF format.
    /// </summary>
    /// <param name="image">The image to encode.</param>
    /// <param name="stream">The output stream.</param>
    public static void Encode(Image image, Stream stream)
    {
        Encode(image, stream, TiffCompression.None, TiffPredictor.None);
    }

    /// <summary>
    /// Encodes an image to TIFF format with specified compression.
    /// </summary>
    /// <param name="image">The image to encode.</param>
    /// <param name="stream">The output stream.</param>
    /// <param name="compression">The compression method.</param>
    public static void Encode(Image image, Stream stream, TiffCompression compression)
    {
        // Use horizontal predictor with LZW/Deflate for better compression
        var predictor = (compression == TiffCompression.Lzw || compression == TiffCompression.Deflate)
            ? TiffPredictor.Horizontal
            : TiffPredictor.None;

        Encode(image, stream, compression, predictor);
    }

    /// <summary>
    /// Encodes an image to TIFF format with specified compression and predictor.
    /// </summary>
    /// <param name="image">The image to encode.</param>
    /// <param name="stream">The output stream.</param>
    /// <param name="compression">The compression method.</param>
    /// <param name="predictor">The predictor to use.</param>
    public static void Encode(Image image, Stream stream, TiffCompression compression, TiffPredictor predictor)
    {
        var encoder = new TiffEncoder(stream, compression, predictor);
        encoder.Encode(image);
    }

    /// <summary>
    /// Encodes multiple images as a multi-page TIFF.
    /// </summary>
    /// <param name="images">The images to encode.</param>
    /// <param name="stream">The output stream.</param>
    public static void EncodeMultiPage(IList<Image> images, Stream stream)
    {
        EncodeMultiPage(images, stream, TiffCompression.None);
    }

    /// <summary>
    /// Encodes multiple images as a multi-page TIFF with compression.
    /// </summary>
    /// <param name="images">The images to encode.</param>
    /// <param name="stream">The output stream.</param>
    /// <param name="compression">The compression method.</param>
    public static void EncodeMultiPage(IList<Image> images, Stream stream, TiffCompression compression)
    {
        var predictor = (compression == TiffCompression.Lzw || compression == TiffCompression.Deflate)
            ? TiffPredictor.Horizontal
            : TiffPredictor.None;

        var encoder = new TiffEncoder(stream, compression, predictor);
        encoder.Encode(images);
    }
}
