using System;
using System.Collections.Generic;
using System.IO;

namespace TinyImage.Codecs.Ico;

/// <summary>
/// ICO/CUR codec for encoding and decoding Windows icon and cursor files.
/// </summary>
/// <remarks>
/// ICO files (.ico) store a collection of images at different sizes and color depths.
/// Individual images can be encoded as BMP or PNG format.
/// CUR files (.cur) use the same format but include hotspot coordinates for each image.
/// 
/// Format-specific metadata is stored in the Image and can be accessed via:
/// <code>
/// var metadata = image.GetMetadata&lt;IcoMetadata&gt;();
/// </code>
/// </remarks>
internal static class IcoCodec
{
    /// <summary>
    /// Decodes an ICO or CUR image from a stream.
    /// </summary>
    /// <param name="stream">The stream containing ICO/CUR data.</param>
    /// <returns>The decoded image with all entries as frames. ICO metadata is stored in the image.</returns>
    /// <exception cref="ArgumentNullException">Stream is null.</exception>
    /// <exception cref="InvalidOperationException">Invalid ICO/CUR data.</exception>
    public static Image Decode(Stream stream)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        var decoder = new IcoDecoder(stream);
        var (resourceType, entries, images) = decoder.Decode();

        if (images.Count == 0)
            throw new InvalidOperationException("ICO/CUR file contains no images.");

        // Create metadata
        var metadata = new IcoMetadata { ResourceType = resourceType };

        // Create frames from decoded images
        var frames = new List<ImageFrame>(images.Count);
        bool hasAlpha = false;

        for (int i = 0; i < images.Count; i++)
        {
            var (width, height, rgba) = images[i];
            var entry = entries[i];

            var buffer = new PixelBuffer((int)width, (int)height, rgba);
            var frame = new ImageFrame(buffer);
            frames.Add(frame);

            // Create entry metadata
            var entryMetadata = new IcoEntryMetadata
            {
                BitsPerPixel = entry.BitsPerPixel,
                IsPng = entry.IsPng
            };

            // Set cursor hotspot if applicable
            if (entry.CursorHotspot.HasValue)
            {
                entryMetadata.HotspotX = entry.CursorHotspot.Value.X;
                entryMetadata.HotspotY = entry.CursorHotspot.Value.Y;
            }

            metadata.AddEntry(entryMetadata);

            // Check for alpha
            if (!hasAlpha)
            {
                for (int j = 3; j < rgba.Length; j += 4)
                {
                    if (rgba[j] != 255)
                    {
                        hasAlpha = true;
                        break;
                    }
                }
            }
        }

        var image = new Image(frames, hasAlpha);
        image.SetMetadata(metadata);
        return image;
    }

    /// <summary>
    /// Encodes an image to ICO format.
    /// </summary>
    /// <param name="image">The image to encode.</param>
    /// <param name="stream">The stream to write to.</param>
    /// <exception cref="ArgumentNullException">Image or stream is null.</exception>
    public static void Encode(Image image, Stream stream)
    {
        Encode(image, stream, IcoResourceType.Icon);
    }

    /// <summary>
    /// Encodes an image to ICO or CUR format.
    /// </summary>
    /// <param name="image">The image to encode.</param>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="resourceType">The type of resource (Icon or Cursor).</param>
    /// <exception cref="ArgumentNullException">Image or stream is null.</exception>
    /// <remarks>
    /// If the image has IcoMetadata with cursor hotspots, those will be used.
    /// Otherwise, hotspots default to (0, 0).
    /// </remarks>
    public static void Encode(Image image, Stream stream, IcoResourceType resourceType)
    {
        if (image == null)
            throw new ArgumentNullException(nameof(image));
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        var encoder = new IcoEncoder(stream);

        // Get metadata if present
        var metadata = image.GetMetadata<IcoMetadata>();

        // Build list of images from frames
        var images = new List<(int width, int height, byte[] rgba, (ushort x, ushort y)? hotspot)>();

        int frameIndex = 0;
        foreach (var frame in image.Frames)
        {
            var buffer = frame.Buffer;
            var rgba = buffer.GetRawData();

            // Get hotspot from metadata if available
            (ushort x, ushort y)? hotspot = null;
            if (resourceType == IcoResourceType.Cursor && metadata != null)
            {
                var entryMetadata = metadata.GetEntry(frameIndex);
                if (entryMetadata != null)
                {
                    hotspot = (entryMetadata.HotspotX, entryMetadata.HotspotY);
                }
            }

            images.Add((buffer.Width, buffer.Height, rgba, hotspot));
            frameIndex++;
        }

        encoder.Encode(resourceType, images);
    }

    /// <summary>
    /// Checks if the data appears to be a valid ICO file by checking the header.
    /// </summary>
    /// <param name="data">The data to check (must be at least 4 bytes).</param>
    /// <returns>True if the data appears to be an ICO file.</returns>
    public static bool IsIco(ReadOnlySpan<byte> data)
    {
        // ICO: reserved=0, type=1
        return data.Length >= 4 &&
               data[0] == 0x00 && data[1] == 0x00 &&
               data[2] == 0x01 && data[3] == 0x00;
    }

    /// <summary>
    /// Checks if the data appears to be a valid CUR file by checking the header.
    /// </summary>
    /// <param name="data">The data to check (must be at least 4 bytes).</param>
    /// <returns>True if the data appears to be a CUR file.</returns>
    public static bool IsCur(ReadOnlySpan<byte> data)
    {
        // CUR: reserved=0, type=2
        return data.Length >= 4 &&
               data[0] == 0x00 && data[1] == 0x00 &&
               data[2] == 0x02 && data[3] == 0x00;
    }

    /// <summary>
    /// Checks if the stream appears to contain ICO data by checking the header.
    /// </summary>
    /// <param name="stream">The stream to check.</param>
    /// <returns>True if the stream appears to contain ICO data.</returns>
    public static bool IsIco(Stream stream)
    {
        if (stream == null || !stream.CanRead)
            return false;

        long originalPosition = stream.CanSeek ? stream.Position : 0;

        try
        {
            byte[] header = new byte[4];
            if (stream.Read(header, 0, 4) != 4)
                return false;

            return header[0] == 0x00 && header[1] == 0x00 &&
                   header[2] == 0x01 && header[3] == 0x00;
        }
        finally
        {
            if (stream.CanSeek)
                stream.Position = originalPosition;
        }
    }

    /// <summary>
    /// Checks if the stream appears to contain CUR data by checking the header.
    /// </summary>
    /// <param name="stream">The stream to check.</param>
    /// <returns>True if the stream appears to contain CUR data.</returns>
    public static bool IsCur(Stream stream)
    {
        if (stream == null || !stream.CanRead)
            return false;

        long originalPosition = stream.CanSeek ? stream.Position : 0;

        try
        {
            byte[] header = new byte[4];
            if (stream.Read(header, 0, 4) != 4)
                return false;

            return header[0] == 0x00 && header[1] == 0x00 &&
                   header[2] == 0x02 && header[3] == 0x00;
        }
        finally
        {
            if (stream.CanSeek)
                stream.Position = originalPosition;
        }
    }
}
