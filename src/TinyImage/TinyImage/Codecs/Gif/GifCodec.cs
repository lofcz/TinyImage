using System;
using System.Collections.Generic;
using System.IO;

namespace TinyImage.Codecs.Gif;

/// <summary>
/// GIF codec for encoding and decoding GIF images.
/// Supports both static and animated GIFs.
/// Based on UniGif (MIT) for decoding and AnimatedGifEncoder (MIT) for encoding.
/// </summary>
internal static class GifCodec
{
    /// <summary>
    /// Decodes a GIF image from a stream.
    /// </summary>
    /// <param name="stream">The stream containing GIF data.</param>
    /// <returns>The decoded image with all frames.</returns>
    public static Image Decode(Stream stream)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        // Read entire stream into byte array
        byte[] data;
        if (stream is MemoryStream ms && ms.TryGetBuffer(out var segment))
        {
            data = new byte[segment.Count];
            Array.Copy(segment.Array!, segment.Offset, data, 0, segment.Count);
        }
        else
        {
            using var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            data = memoryStream.ToArray();
        }

        // Parse GIF structure
        var gifData = GifDecoder.Parse(data);

        if (gifData.ImageBlocks == null || gifData.ImageBlocks.Count == 0)
            throw new InvalidOperationException("GIF contains no image data.");

        // Decode all frames with proper frame composition
        var frames = new List<ImageFrame>();
        bool hasTransparency = false;

        int canvasWidth = gifData.LogicalScreenWidth;
        int canvasHeight = gifData.LogicalScreenHeight;

        // Persistent canvas for frame composition
        var canvas = new Rgba32[canvasWidth * canvasHeight];
        var previousCanvas = new Rgba32[canvasWidth * canvasHeight];

        // Initialize canvas with background color
        Rgba32 bgColor = new Rgba32(0, 0, 0, 0);
        if (gifData.GlobalColorTable != null && gifData.BackgroundColorIndex < gifData.GlobalColorTable.Count)
        {
            var bgRgb = gifData.GlobalColorTable[gifData.BackgroundColorIndex];
            bgColor = new Rgba32(bgRgb[0], bgRgb[1], bgRgb[2], 255);
        }

        for (int i = 0; i < canvas.Length; i++)
        {
            canvas[i] = bgColor;
            previousCanvas[i] = bgColor;
        }

        for (int i = 0; i < gifData.ImageBlocks.Count; i++)
        {
            var imageBlock = gifData.ImageBlocks[i];

            // Get graphic control extension for this frame (if any)
            GifGraphicControlExtension? gcExt = null;
            if (gifData.GraphicControlExtensions != null && i < gifData.GraphicControlExtensions.Count)
            {
                gcExt = gifData.GraphicControlExtensions[i];
            }

            // Get disposal method for this frame
            int disposalMethod = gcExt.HasValue ? gcExt.Value.DisposalMethod : 0;

            // Save canvas state before drawing if disposal method is 3 (restore to previous)
            if (disposalMethod == 3)
            {
                Array.Copy(canvas, previousCanvas, canvas.Length);
            }

            // Decode and draw frame onto canvas
            var frame = DecodeFrameOntoCanvas(gifData, imageBlock, gcExt, canvas, canvasWidth, canvasHeight, ref hasTransparency);
            frames.Add(frame);

            // Apply disposal method AFTER copying the frame to output
            ApplyDisposalMethod(disposalMethod, canvas, previousCanvas, canvasWidth, canvasHeight, 
                imageBlock, bgColor, gcExt);
        }

        // Get loop count from NETSCAPE extension
        int loopCount = gifData.ApplicationExtension.LoopCount;

        return new Image(frames, hasTransparency, loopCount);
    }

    /// <summary>
    /// Encodes an image to GIF format.
    /// </summary>
    /// <param name="image">The image to encode.</param>
    /// <param name="stream">The stream to write to.</param>
    public static void Encode(Image image, Stream stream)
    {
        if (image == null)
            throw new ArgumentNullException(nameof(image));
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        var encoder = new GifEncoder(stream);
        encoder.WriteImage(image);
    }

    private static ImageFrame DecodeFrameOntoCanvas(
        GifData gifData,
        GifImageBlock imageBlock,
        GifGraphicControlExtension? gcExt,
        Rgba32[] canvas,
        int canvasWidth,
        int canvasHeight,
        ref bool hasTransparency)
    {
        // Get color table
        var colorTable = GifDecoder.GetColorTable(gifData, imageBlock);
        if (colorTable == null)
            throw new InvalidOperationException("GIF frame has no color table.");

        // Decode image data
        var indexedPixels = GifDecoder.DecodeImageData(imageBlock);

        // Get transparency info
        int transparentIndex = -1;
        if (gcExt.HasValue && gcExt.Value.TransparentColorFlag)
        {
            transparentIndex = gcExt.Value.TransparentColorIndex;
            hasTransparency = true;
        }

        // Draw the image block onto the canvas
        int left = imageBlock.ImageLeftPosition;
        int top = imageBlock.ImageTopPosition;
        int width = imageBlock.ImageWidth;
        int height = imageBlock.ImageHeight;

        int pixelIndex = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (pixelIndex >= indexedPixels.Length)
                    break;

                int colorIndex = indexedPixels[pixelIndex++];

                // Check if this pixel is transparent - don't modify canvas
                if (colorIndex == transparentIndex)
                    continue;

                if (colorIndex >= colorTable.Count)
                    continue;

                var rgb = colorTable[colorIndex];
                var color = new Rgba32(rgb[0], rgb[1], rgb[2], 255);

                int destX = left + x;
                int destY = top + y;

                if (destX >= 0 && destX < canvasWidth && destY >= 0 && destY < canvasHeight)
                {
                    canvas[destY * canvasWidth + destX] = color;
                }
            }
        }

        // Copy canvas to a new buffer for this frame
        var buffer = new PixelBuffer(canvasWidth, canvasHeight);
        for (int y = 0; y < canvasHeight; y++)
        {
            for (int x = 0; x < canvasWidth; x++)
            {
                buffer.SetPixel(x, y, canvas[y * canvasWidth + x]);
            }
        }

        // Create frame with duration
        var frame = new ImageFrame(buffer);
        if (gcExt.HasValue)
        {
            // Delay time is in hundredths of a second
            frame.Duration = TimeSpan.FromMilliseconds(gcExt.Value.DelayTime * 10);
        }

        return frame;
    }

    private static void ApplyDisposalMethod(
        int disposalMethod,
        Rgba32[] canvas,
        Rgba32[] previousCanvas,
        int canvasWidth,
        int canvasHeight,
        GifImageBlock imageBlock,
        Rgba32 bgColor,
        GifGraphicControlExtension? gcExt)
    {
        switch (disposalMethod)
        {
            case 0: // No disposal specified - treat as "do not dispose"
            case 1: // Do not dispose - leave canvas as is
                // Canvas already contains the drawn frame, nothing to do
                break;

            case 2: // Restore to background color
                // Clear the frame area to background color
                int left = imageBlock.ImageLeftPosition;
                int top = imageBlock.ImageTopPosition;
                int width = imageBlock.ImageWidth;
                int height = imageBlock.ImageHeight;

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int destX = left + x;
                        int destY = top + y;

                        if (destX >= 0 && destX < canvasWidth && destY >= 0 && destY < canvasHeight)
                        {
                            canvas[destY * canvasWidth + destX] = bgColor;
                        }
                    }
                }
                break;

            case 3: // Restore to previous
                // Restore canvas to state before this frame was drawn
                Array.Copy(previousCanvas, canvas, canvas.Length);
                break;
        }
    }
}
