using System;
using System.Collections.Generic;
using System.IO;

namespace TinyImage;

/// <summary>
/// Represents an image that can be loaded, saved, and manipulated.
/// This is the main entry point for the TinyImage library.
/// </summary>
/// <remarks>
/// Images contain one or more frames. Single-frame formats (PNG, JPEG) have exactly one frame,
/// while animated formats (GIF, WebP, APNG) may have multiple frames with timing information.
/// </remarks>
public sealed class Image
{
    /// <summary>
    /// Gets the collection of frames in this image.
    /// </summary>
    /// <remarks>
    /// For single-frame images, this collection contains exactly one frame.
    /// For animated images, this collection contains multiple frames with duration information.
    /// </remarks>
    public ImageFrameCollection Frames { get; }

    /// <summary>
    /// Gets the width of the image in pixels.
    /// </summary>
    /// <remarks>
    /// Returns the width of the root (first) frame.
    /// </remarks>
    public int Width => Frames.RootFrame.Width;

    /// <summary>
    /// Gets the height of the image in pixels.
    /// </summary>
    /// <remarks>
    /// Returns the height of the root (first) frame.
    /// </remarks>
    public int Height => Frames.RootFrame.Height;

    /// <summary>
    /// Gets whether this image has an alpha channel.
    /// </summary>
    /// <remarks>
    /// All images are stored internally as RGBA32, but this property indicates
    /// whether the original image had meaningful alpha values.
    /// </remarks>
    public bool HasAlpha { get; }

    /// <summary>
    /// Gets or sets the number of times an animated image should loop.
    /// </summary>
    /// <remarks>
    /// A value of 0 means infinite looping.
    /// A value of 1 means play once (no loop).
    /// This property is used for animated formats like GIF, WebP, and APNG.
    /// </remarks>
    public int LoopCount { get; set; } = 0;

    /// <summary>
    /// Creates a new image with the specified dimensions.
    /// </summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="hasAlpha">Whether the image has an alpha channel.</param>
    public Image(int width, int height, bool hasAlpha = true)
    {
        Frames = new ImageFrameCollection(width, height);
        HasAlpha = hasAlpha;
    }

    /// <summary>
    /// Creates a new image from an existing pixel buffer.
    /// </summary>
    internal Image(PixelBuffer buffer, bool hasAlpha)
    {
        if (buffer == null)
            throw new ArgumentNullException(nameof(buffer));
        Frames = new ImageFrameCollection(new ImageFrame(buffer));
        HasAlpha = hasAlpha;
    }

    /// <summary>
    /// Creates a new image from an existing frame.
    /// </summary>
    internal Image(ImageFrame frame, bool hasAlpha)
    {
        if (frame == null)
            throw new ArgumentNullException(nameof(frame));
        Frames = new ImageFrameCollection(frame);
        HasAlpha = hasAlpha;
    }

    /// <summary>
    /// Creates a new image from a collection of frames.
    /// </summary>
    internal Image(IEnumerable<ImageFrame> frames, bool hasAlpha, int loopCount = 0)
    {
        if (frames == null)
            throw new ArgumentNullException(nameof(frames));
        Frames = new ImageFrameCollection(frames);
        HasAlpha = hasAlpha;
        LoopCount = loopCount;
    }

    /// <summary>
    /// Creates a new image from an existing frame collection.
    /// </summary>
    internal Image(ImageFrameCollection frames, bool hasAlpha, int loopCount = 0)
    {
        Frames = frames ?? throw new ArgumentNullException(nameof(frames));
        HasAlpha = hasAlpha;
        LoopCount = loopCount;
    }

    #region Pixel Access

    /// <summary>
    /// Gets the pixel color at the specified coordinates in the root frame.
    /// </summary>
    /// <param name="x">The x coordinate (column).</param>
    /// <param name="y">The y coordinate (row).</param>
    /// <returns>The pixel color at the specified location.</returns>
    public Rgba32 GetPixel(int x, int y) => Frames.RootFrame.GetPixel(x, y);

    /// <summary>
    /// Sets the pixel color at the specified coordinates in the root frame.
    /// </summary>
    /// <param name="x">The x coordinate (column).</param>
    /// <param name="y">The y coordinate (row).</param>
    /// <param name="color">The color to set.</param>
    public void SetPixel(int x, int y, Rgba32 color) => Frames.RootFrame.SetPixel(x, y, color);

    /// <summary>
    /// Gets the internal pixel buffer of the root frame for codec access.
    /// </summary>
    internal PixelBuffer GetBuffer() => Frames.RootFrame.Buffer;

    #endregion

    #region Load Methods

    /// <summary>
    /// Loads an image from a file.
    /// </summary>
    /// <param name="path">The path to the image file.</param>
    /// <returns>The loaded image.</returns>
    /// <exception cref="ArgumentNullException">Path is null.</exception>
    /// <exception cref="FileNotFoundException">File does not exist.</exception>
    /// <exception cref="NotSupportedException">Image format is not supported.</exception>
    public static Image Load(string path)
    {
        if (path == null)
            throw new ArgumentNullException(nameof(path));
        if (!File.Exists(path))
            throw new FileNotFoundException("Image file not found.", path);

        var format = DetectFormat(path);
        using var stream = File.OpenRead(path);
        return Load(stream, format);
    }

    /// <summary>
    /// Loads an image from a stream.
    /// </summary>
    /// <param name="stream">The stream containing image data.</param>
    /// <param name="format">The image format.</param>
    /// <returns>The loaded image.</returns>
    /// <exception cref="ArgumentNullException">Stream is null.</exception>
    public static Image Load(Stream stream, ImageFormat format)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        return format switch
        {
            ImageFormat.Png => Codecs.Png.PngCodec.Decode(stream),
            ImageFormat.Jpeg => Codecs.Jpeg.JpegCodec.Decode(stream),
            ImageFormat.Gif => Codecs.Gif.GifCodec.Decode(stream),
            ImageFormat.Jpeg2000 => Codecs.Jpeg2000.Jpeg2000Codec.Decode(stream),
            ImageFormat.Bmp => Codecs.Bmp.BmpCodec.Decode(stream),
            ImageFormat.Pbm or ImageFormat.Pgm or ImageFormat.Ppm => Codecs.Pnm.PnmCodec.Decode(stream),
            ImageFormat.WebP => Codecs.WebP.WebPCodec.Decode(stream),
            ImageFormat.Tiff => Codecs.Tiff.TiffCodec.Decode(stream),
            ImageFormat.Tga => Codecs.Tga.TgaCodec.Decode(stream),
            _ => throw new NotSupportedException($"Image format '{format}' is not supported.")
        };
    }

    /// <summary>
    /// Loads an image from a byte array.
    /// </summary>
    /// <param name="data">The image data.</param>
    /// <returns>The loaded image.</returns>
    /// <exception cref="ArgumentNullException">Data is null.</exception>
    public static Image Load(byte[] data)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        var format = DetectFormat(data);
        using var stream = new MemoryStream(data);
        return Load(stream, format);
    }

    /// <summary>
    /// Loads an image from a span of bytes.
    /// </summary>
    /// <param name="data">The image data.</param>
    /// <returns>The loaded image.</returns>
    public static Image Load(ReadOnlySpan<byte> data)
    {
        var format = DetectFormat(data);
        // Note: For netstandard2.0 compatibility, we copy to array
        // On modern TFMs, we could use MemoryStream(byte[]) with ArrayPool
        var array = data.ToArray();
        using var stream = new MemoryStream(array);
        return Load(stream, format);
    }

    #endregion

    #region Save Methods

    /// <summary>
    /// Saves the image to a file.
    /// </summary>
    /// <param name="path">The path to save the image to.</param>
    /// <exception cref="ArgumentNullException">Path is null.</exception>
    public void Save(string path)
    {
        if (path == null)
            throw new ArgumentNullException(nameof(path));

        var format = DetectFormat(path);
        using var stream = File.Create(path);
        Save(stream, format);
    }

    /// <summary>
    /// Saves the image to a stream.
    /// </summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="format">The image format to use.</param>
    /// <exception cref="ArgumentNullException">Stream is null.</exception>
    public void Save(Stream stream, ImageFormat format)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        switch (format)
        {
            case ImageFormat.Png:
                Codecs.Png.PngCodec.Encode(this, stream);
                break;
            case ImageFormat.Jpeg:
                Codecs.Jpeg.JpegCodec.Encode(this, stream);
                break;
            case ImageFormat.Gif:
                Codecs.Gif.GifCodec.Encode(this, stream);
                break;
            case ImageFormat.Jpeg2000:
                Codecs.Jpeg2000.Jpeg2000Codec.Encode(this, stream);
                break;
            case ImageFormat.Bmp:
                Codecs.Bmp.BmpCodec.Encode(this, stream);
                break;
            case ImageFormat.Pbm:
                Codecs.Pnm.PnmCodec.EncodePbm(this, stream);
                break;
            case ImageFormat.Pgm:
                Codecs.Pnm.PnmCodec.EncodePgm(this, stream);
                break;
            case ImageFormat.Ppm:
                Codecs.Pnm.PnmCodec.EncodePpm(this, stream);
                break;
            case ImageFormat.WebP:
                Codecs.WebP.WebPCodec.Encode(this, stream);
                break;
            case ImageFormat.Tiff:
                Codecs.Tiff.TiffCodec.Encode(this, stream);
                break;
            case ImageFormat.Tga:
                Codecs.Tga.TgaCodec.Encode(this, stream);
                break;
            default:
                throw new NotSupportedException($"Image format '{format}' is not supported.");
        }
    }

    /// <summary>
    /// Converts the image to a byte array.
    /// </summary>
    /// <param name="format">The image format to use.</param>
    /// <returns>The image data as a byte array.</returns>
    public byte[] ToArray(ImageFormat format)
    {
        using var stream = new MemoryStream();
        Save(stream, format);
        return stream.ToArray();
    }

    #endregion

    #region Resize

    /// <summary>
    /// Creates a resized copy of this image.
    /// </summary>
    /// <param name="newWidth">The new width in pixels.</param>
    /// <param name="newHeight">The new height in pixels.</param>
    /// <param name="mode">The resize interpolation mode. Default is bilinear.</param>
    /// <returns>A new resized image.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Width or height is not positive.</exception>
    /// <remarks>
    /// For animated images, all frames are resized and frame durations are preserved.
    /// </remarks>
    public Image Resize(int newWidth, int newHeight, ResizeMode mode = ResizeMode.Bilinear)
    {
        if (newWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(newWidth), "Width must be positive.");
        if (newHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(newHeight), "Height must be positive.");

        return TinyImage.Resize.Resizer.Resize(this, newWidth, newHeight, mode);
    }

    #endregion

    #region Clone

    /// <summary>
    /// Creates a deep copy of this image, including all frames.
    /// </summary>
    /// <returns>A new image with copied pixel data.</returns>
    public Image Clone()
    {
        return new Image(Frames.Clone(), HasAlpha, LoopCount);
    }

    #endregion

    #region Format Detection

    private static ImageFormat DetectFormat(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".png" => ImageFormat.Png,
            ".jpg" or ".jpeg" => ImageFormat.Jpeg,
            ".gif" => ImageFormat.Gif,
            ".jp2" or ".j2k" or ".j2c" or ".jpf" or ".jpx" => ImageFormat.Jpeg2000,
            ".bmp" or ".dib" => ImageFormat.Bmp,
            ".pbm" => ImageFormat.Pbm,
            ".pgm" => ImageFormat.Pgm,
            ".ppm" or ".pnm" => ImageFormat.Ppm,
            ".webp" => ImageFormat.WebP,
            ".tif" or ".tiff" => ImageFormat.Tiff,
            ".tga" or ".vda" or ".icb" or ".vst" => ImageFormat.Tga,
            _ => throw new NotSupportedException($"Unknown image format for extension '{ext}'.")
        };
    }

    private static ImageFormat DetectFormat(byte[] data)
    {
        return DetectFormat(data.AsSpan());
    }

    private static ImageFormat DetectFormat(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8)
            throw new ArgumentException("Data is too short to detect image format.");

        // PNG signature: 89 50 4E 47 0D 0A 1A 0A
        if (data.Length >= 8 &&
            data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
            data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A)
        {
            return ImageFormat.Png;
        }

        // JPEG signature: FF D8 FF
        if (data.Length >= 3 &&
            data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            return ImageFormat.Jpeg;
        }

        // GIF signature: 47 49 46 (GIF)
        if (data.Length >= 6 &&
            data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46 &&  // "GIF"
            data[3] == 0x38 && (data[4] == 0x37 || data[4] == 0x39) && data[5] == 0x61)  // "87a" or "89a"
        {
            return ImageFormat.Gif;
        }

        // JPEG 2000 JP2 container signature: 00 00 00 0C 6A 50 20 20 (jP  )
        if (data.Length >= 12 &&
            data[0] == 0x00 && data[1] == 0x00 && data[2] == 0x00 && data[3] == 0x0C &&
            data[4] == 0x6A && data[5] == 0x50 && data[6] == 0x20 && data[7] == 0x20)
        {
            return ImageFormat.Jpeg2000;
        }

        // JPEG 2000 raw codestream signature: FF 4F FF 51 (SOC + SIZ markers)
        if (data.Length >= 4 &&
            data[0] == 0xFF && data[1] == 0x4F && data[2] == 0xFF && data[3] == 0x51)
        {
            return ImageFormat.Jpeg2000;
        }

        // BMP signature: 42 4D ("BM")
        if (data.Length >= 2 &&
            data[0] == 0x42 && data[1] == 0x4D)
        {
            return ImageFormat.Bmp;
        }

        // Netpbm formats: P1-P6 (0x50 + 0x31-0x36)
        if (data.Length >= 2 && data[0] == 0x50) // 'P'
        {
            switch (data[1])
            {
                case 0x31: // P1 - PBM ASCII
                case 0x34: // P4 - PBM Binary
                    return ImageFormat.Pbm;
                case 0x32: // P2 - PGM ASCII
                case 0x35: // P5 - PGM Binary
                    return ImageFormat.Pgm;
                case 0x33: // P3 - PPM ASCII
                case 0x36: // P6 - PPM Binary
                    return ImageFormat.Ppm;
            }
        }

        // WebP signature: RIFF....WEBP
        if (data.Length >= 12 &&
            data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46 && // "RIFF"
            data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50)  // "WEBP"
        {
            return ImageFormat.WebP;
        }

        // TIFF signature: II* (little-endian) or MM* (big-endian)
        if (data.Length >= 4)
        {
            // Little-endian: 0x49 0x49 0x2A 0x00 ("II" + 42)
            if (data[0] == 0x49 && data[1] == 0x49 && data[2] == 0x2A && data[3] == 0x00)
            {
                return ImageFormat.Tiff;
            }
            // Big-endian: 0x4D 0x4D 0x00 0x2A ("MM" + 42)
            if (data[0] == 0x4D && data[1] == 0x4D && data[2] == 0x00 && data[3] == 0x2A)
            {
                return ImageFormat.Tiff;
            }
        }

        throw new NotSupportedException("Unknown image format. Could not detect from file signature.");
    }

    #endregion
}
