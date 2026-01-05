using System;
using System.Collections.Generic;
using System.IO;
using TinyImage.Codecs.Ico;

namespace TinyImage.Codecs.Ani;

/// <summary>
/// Codec for ANI (Animated Cursor) files.
/// </summary>
internal static class AniCodec
{
    /// <summary>
    /// Magic bytes for ANI files: "RIFF" followed by size then "ACON".
    /// </summary>
    private static readonly byte[] RiffMagic = { 0x52, 0x49, 0x46, 0x46 }; // "RIFF"
    private static readonly byte[] AconMagic = { 0x41, 0x43, 0x4F, 0x4E }; // "ACON"

    /// <summary>
    /// Checks if the data represents an ANI file.
    /// </summary>
    public static bool IsAni(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12)
            return false;

        // Check for "RIFF" signature
        if (data[0] != RiffMagic[0] || data[1] != RiffMagic[1] ||
            data[2] != RiffMagic[2] || data[3] != RiffMagic[3])
            return false;

        // Check for "ACON" form type at offset 8
        if (data[8] != AconMagic[0] || data[9] != AconMagic[1] ||
            data[10] != AconMagic[2] || data[11] != AconMagic[3])
            return false;

        return true;
    }

    /// <summary>
    /// Decodes an ANI file from a stream.
    /// </summary>
    public static Image Decode(Stream stream)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        var decoder = new AniDecoder(stream);
        var (header, metadata, frameData) = decoder.Decode();

        if (frameData.Count == 0)
            throw new InvalidOperationException("ANI file contains no frames.");

        var frames = new List<ImageFrame>(frameData.Count);
        bool hasAlpha = false;

        // Decode each frame using ICO codec
        for (int i = 0; i < frameData.Count; i++)
        {
            using var frameStream = new MemoryStream(frameData[i]);

            // Try to decode as ICO/CUR
            Image frameImage;
            try
            {
                frameImage = IcoCodec.Decode(frameStream);
            }
            catch
            {
                throw new InvalidOperationException($"Failed to decode frame {i} as ICO/CUR.");
            }

            // Extract the first frame from the ICO
            if (frameImage.Frames.Count > 0)
            {
                var frame = frameImage.Frames[0];
                frames.Add(new ImageFrame(frame.Buffer));

                if (!hasAlpha && frameImage.HasAlpha)
                    hasAlpha = true;

                // Extract hotspot if available
                var icoMeta = frameImage.GetMetadata<IcoMetadata>();
                if (icoMeta?.Entries.Count > 0)
                {
                    var entry = icoMeta.Entries[0];
                    if (entry.HotspotX != 0 || entry.HotspotY != 0)
                    {
                        metadata.SetHotspot(i, entry.HotspotX, entry.HotspotY);
                    }
                }
            }
        }

        if (frames.Count == 0)
            throw new InvalidOperationException("ANI file contains no valid frames.");

        var image = new Image(frames, hasAlpha);
        image.SetMetadata(metadata);

        return image;
    }

    /// <summary>
    /// Encodes an image as an ANI file.
    /// </summary>
    public static void Encode(Image image, Stream stream)
    {
        if (image == null)
            throw new ArgumentNullException(nameof(image));
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        var metadata = image.GetMetadata<AniMetadata>() ?? new AniMetadata();
        var frameData = new List<byte[]>();

        // Encode each frame as CUR (to preserve hotspots)
        int frameIndex = 0;
        foreach (var frame in image.Frames)
        {
            using var frameStream = new MemoryStream();

            // Create a single-frame image for encoding
            var singleFrameImage = new Image(new[] { new ImageFrame(frame.Buffer) }, image.HasAlpha);

            // Set up ICO metadata with hotspot
            var icoMeta = new IcoMetadata { ResourceType = IcoResourceType.Cursor };

            if (metadata.Hotspots != null && frameIndex < metadata.Hotspots.Count)
            {
                var (x, y) = metadata.Hotspots[frameIndex];
                icoMeta.SetHotspot(0, x, y);
            }
            else
            {
                icoMeta.SetHotspot(0, 0, 0);
            }

            singleFrameImage.SetMetadata(icoMeta);

            // Encode as CUR
            IcoCodec.Encode(singleFrameImage, frameStream, IcoResourceType.Cursor);
            frameData.Add(frameStream.ToArray());

            frameIndex++;
        }

        var encoder = new AniEncoder(stream);
        encoder.Encode(frameData, metadata);
    }
}
