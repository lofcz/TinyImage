// JPEG 2000 codec wrapper for TinyImage
// Based on TinyImage.Codecs.Jpeg2000 - BSD 3-Clause License
// Copyright (c) 1999-2000 JJ2000 Partners
// Copyright (c) 2007-2012 Jason S. Clary
// Copyright (c) 2013-2016 Anders Gustafsson, Cureos AB
// Copyright (c) 2024-2025 Sjofn LLC

using System;
using System.IO;
using TinyImage.Codecs.Jpeg2000.j2k.image;
using TinyImage.Codecs.Jpeg2000.j2k.util;
using TinyImage.Codecs.Jpeg2000.Util;

namespace TinyImage.Codecs.Jpeg2000;

/// <summary>
/// JPEG 2000 codec for encoding and decoding JPEG 2000 images (.jp2, .j2k, .j2c).
/// Based on TinyImage.Codecs.Jpeg2000 (BSD 3-Clause License).
/// Supports both lossless and lossy compression with full JPEG 2000 Part 1 compliance.
/// </summary>
internal static class Jpeg2000Codec
{
    /// <summary>
    /// Decodes a JPEG 2000 image from a stream.
    /// </summary>
    /// <param name="stream">The stream containing JPEG 2000 data.</param>
    /// <returns>The decoded image.</returns>
    public static Image Decode(Stream stream)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        // Decode using TinyImage.Codecs.Jpeg2000
        var interleavedImage = J2kImage.FromStream(stream);

        // Convert InterleavedImage to TinyImage's PixelBuffer
        return ConvertToImage(interleavedImage);
    }

    /// <summary>
    /// Decodes a JPEG 2000 image from a byte array.
    /// </summary>
    /// <param name="data">The JPEG 2000 data.</param>
    /// <returns>The decoded image.</returns>
    public static Image Decode(byte[] data)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        var interleavedImage = J2kImage.FromBytes(data);
        return ConvertToImage(interleavedImage);
    }

    /// <summary>
    /// Encodes an image to JPEG 2000 format.
    /// </summary>
    /// <param name="image">The image to encode.</param>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="lossless">If true, uses lossless compression. Default is false (lossy).</param>
    public static void Encode(Image image, Stream stream, bool lossless = false)
    {
        if (image == null)
            throw new ArgumentNullException(nameof(image));
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        // Convert TinyImage to BlkImgDataSrc
        var source = ConvertToImageSource(image);

        // Create encoder parameters
        var parameters = new ParameterList(J2kImage.GetDefaultEncoderParameterList());
        
        if (lossless)
        {
            parameters["lossless"] = "on";
        }
        
        // Use JP2 file format (with container)
        parameters["file_format"] = "on";

        // Encode
        var data = J2kImage.ToBytes(source, parameters);

        // Write to stream
        stream.Write(data, 0, data.Length);
    }

    /// <summary>
    /// Encodes an image to JPEG 2000 format with specified quality.
    /// </summary>
    /// <param name="image">The image to encode.</param>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="quality">Quality factor (0.0 to 1.0). Higher values = better quality, larger file.</param>
    public static void Encode(Image image, Stream stream, float quality)
    {
        if (image == null)
            throw new ArgumentNullException(nameof(image));
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));
        if (quality < 0 || quality > 1)
            throw new ArgumentOutOfRangeException(nameof(quality), "Quality must be between 0.0 and 1.0.");

        var source = ConvertToImageSource(image);

        var parameters = new ParameterList(J2kImage.GetDefaultEncoderParameterList());
        
        // Convert quality (0-1) to bitrate (bits per pixel)
        // Higher quality = higher bitrate
        // Typical range: 0.1 bpp (low) to 2.0 bpp (high quality)
        float rate = 0.1f + (quality * 1.9f);
        parameters["rate"] = rate.ToString(System.Globalization.CultureInfo.InvariantCulture);
        parameters["file_format"] = "on";

        var data = J2kImage.ToBytes(source, parameters);
        stream.Write(data, 0, data.Length);
    }

    /// <summary>
    /// Converts TinyImage.Codecs.Jpeg2000's InterleavedImage to TinyImage's Image.
    /// </summary>
    private static Image ConvertToImage(InterleavedImage interleavedImage)
    {
        int width = interleavedImage.Width;
        int height = interleavedImage.Height;
        int numComponents = interleavedImage.NumberOfComponents;

        var buffer = new PixelBuffer(width, height);
        bool hasAlpha = numComponents >= 4;

        // Get byte data from each component
        if (numComponents == 1)
        {
            // Grayscale - replicate to RGB
            var gray = interleavedImage.GetComponentBytes(0);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x;
                    byte g = gray[idx];
                    buffer.SetPixel(x, y, new Rgba32(g, g, g, 255));
                }
            }
        }
        else if (numComponents == 2)
        {
            // Grayscale + Alpha
            var gray = interleavedImage.GetComponentBytes(0);
            var alpha = interleavedImage.GetComponentBytes(1);
            hasAlpha = true;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x;
                    byte g = gray[idx];
                    buffer.SetPixel(x, y, new Rgba32(g, g, g, alpha[idx]));
                }
            }
        }
        else if (numComponents == 3)
        {
            // RGB
            var r = interleavedImage.GetComponentBytes(0);
            var g = interleavedImage.GetComponentBytes(1);
            var b = interleavedImage.GetComponentBytes(2);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x;
                    buffer.SetPixel(x, y, new Rgba32(r[idx], g[idx], b[idx], 255));
                }
            }
        }
        else // numComponents >= 4
        {
            // RGBA (or more components - use first 4)
            var r = interleavedImage.GetComponentBytes(0);
            var g = interleavedImage.GetComponentBytes(1);
            var b = interleavedImage.GetComponentBytes(2);
            var a = interleavedImage.GetComponentBytes(3);
            hasAlpha = true;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x;
                    buffer.SetPixel(x, y, new Rgba32(r[idx], g[idx], b[idx], a[idx]));
                }
            }
        }

        return new Image(buffer, hasAlpha);
    }

    /// <summary>
    /// Converts TinyImage's Image to TinyImage.Codecs.Jpeg2000's BlkImgDataSrc for encoding.
    /// </summary>
    private static BlkImgDataSrc ConvertToImageSource(Image image)
    {
        int width = image.Width;
        int height = image.Height;
        var pixelBuffer = image.GetBuffer();

        // Determine number of components based on alpha
        int numComponents = image.HasAlpha ? 4 : 3;
        
        // Extract component data
        var comps = new int[numComponents][];
        for (int c = 0; c < numComponents; c++)
        {
            comps[c] = new int[width * height];
        }

        // Fill component arrays from pixel buffer
        // JPEG2000 uses level shift: values are stored as signed with 0 at mid-range
        // For 8-bit data: subtract 128 to center around 0
        int levelShift = 128;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var pixel = pixelBuffer.GetPixel(x, y);
                int idx = y * width + x;
                
                comps[0][idx] = pixel.R - levelShift;
                comps[1][idx] = pixel.G - levelShift;
                comps[2][idx] = pixel.B - levelShift;
                
                if (numComponents == 4)
                {
                    comps[3][idx] = pixel.A - levelShift;
                }
            }
        }

        // Create signed array (all components are signed due to level shift)
        var signed = new bool[numComponents];
        for (int i = 0; i < numComponents; i++)
        {
            signed[i] = true;
        }

        // 8 bits per component (nominal range bits)
        int rangeBits = 8;

        return new InterleavedImageSource(width, height, numComponents, rangeBits, signed, comps);
    }
}
