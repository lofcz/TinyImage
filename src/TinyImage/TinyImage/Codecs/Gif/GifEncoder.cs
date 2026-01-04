using System;
using System.IO;

namespace TinyImage.Codecs.Gif;

/// <summary>
/// Encodes images to GIF format.
/// Adapted from AnimatedGifEncoder (MIT License).
/// </summary>
internal sealed class GifEncoder
{
    private const int ColorDepth = 8;
    private const int PaletteSize = 7; // 2^(7+1) = 256 colors

    private readonly Stream _stream;
    private int _width;
    private int _height;
    private int _loopCount;
    private bool _headerWritten;
    private byte[]? _globalColorTable;
    private NeuQuant? _globalQuantizer;

    /// <summary>
    /// Creates a new GIF encoder.
    /// </summary>
    /// <param name="stream">The output stream.</param>
    public GifEncoder(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    /// <summary>
    /// Writes an image (single or multi-frame) to GIF format.
    /// </summary>
    public void WriteImage(Image image)
    {
        if (image == null)
            throw new ArgumentNullException(nameof(image));

        _width = image.Width;
        _height = image.Height;
        _loopCount = image.LoopCount;

        // Build global palette from ALL frames for better color representation
        var allPixels = GetAllFramesPixels(image);

        // Quantize colors using all frames with highest quality (sample factor 1)
        var quantizer = new NeuQuant(allPixels, sampleFac: 1);
        _globalColorTable = quantizer.Process();
        _globalQuantizer = quantizer;

        WriteHeader();
        WriteLogicalScreenDescriptor();
        WriteGlobalColorTable();

        if (image.Frames.Count > 1)
        {
            WriteNetscapeExtension();
        }

        // Write all frames
        for (int i = 0; i < image.Frames.Count; i++)
        {
            var frame = image.Frames[i];
            WriteFrame(frame, i == 0);
        }

        WriteTrailer();
    }

    /// <summary>
    /// Gets all pixels from all frames for palette building.
    /// </summary>
    private static byte[] GetAllFramesPixels(Image image)
    {
        int frameCount = image.Frames.Count;
        int pixelsPerFrame = image.Width * image.Height;
        
        // Collect all pixels from all frames
        var allPixels = new byte[frameCount * pixelsPerFrame * 3];
        int offset = 0;
        
        for (int f = 0; f < frameCount; f++)
        {
            var frame = image.Frames[f];
            var buffer = frame.Buffer;
            int width = buffer.Width;
            int height = buffer.Height;
            
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var pixel = buffer.GetPixel(x, y);
                    allPixels[offset++] = pixel.R;
                    allPixels[offset++] = pixel.G;
                    allPixels[offset++] = pixel.B;
                }
            }
        }
        
        return allPixels;
    }

    private void WriteHeader()
    {
        if (_headerWritten) return;
        
        // GIF89a signature
        WriteString("GIF89a");
        _headerWritten = true;
    }

    private void WriteLogicalScreenDescriptor()
    {
        // Logical screen width
        WriteShort(_width);
        // Logical screen height
        WriteShort(_height);

        // Packed byte:
        // Global Color Table Flag = 1
        // Color Resolution = 7 (8 bits)
        // Sort Flag = 0
        // Size of Global Color Table = 7 (2^(7+1) = 256)
        _stream.WriteByte(0x80 | 0x70 | PaletteSize);

        // Background color index
        _stream.WriteByte(0);

        // Pixel aspect ratio (0 = not specified)
        _stream.WriteByte(0);
    }

    private void WriteGlobalColorTable()
    {
        if (_globalColorTable == null) return;

        _stream.Write(_globalColorTable, 0, _globalColorTable.Length);

        // Pad to 768 bytes (256 colors * 3)
        int padding = 768 - _globalColorTable.Length;
        for (int i = 0; i < padding; i++)
        {
            _stream.WriteByte(0);
        }
    }

    private void WriteNetscapeExtension()
    {
        _stream.WriteByte(0x21); // Extension introducer
        _stream.WriteByte(0xFF); // Application extension label
        _stream.WriteByte(11);   // Block size

        WriteString("NETSCAPE2.0");

        _stream.WriteByte(3);    // Sub-block size
        _stream.WriteByte(1);    // Loop sub-block ID
        WriteShort(_loopCount);  // Loop count
        _stream.WriteByte(0);    // Block terminator
    }

    private void WriteFrame(ImageFrame frame, bool isFirstFrame)
    {
        var pixels = GetRgbPixels(frame);

        // Use global quantizer for consistency, or create new one for this frame
        var quantizer = _globalQuantizer ?? new NeuQuant(pixels);
        if (_globalQuantizer == null)
        {
            quantizer.Process();
        }

        // Get indexed pixels
        var indexedPixels = GetIndexedPixels(pixels, quantizer);

        // Write graphic control extension
        WriteGraphicControlExtension(frame);

        // Write image descriptor
        WriteImageDescriptor(frame.Width, frame.Height);

        // Write pixel data
        WritePixelData(indexedPixels);
    }

    private void WriteGraphicControlExtension(ImageFrame frame)
    {
        _stream.WriteByte(0x21); // Extension introducer
        _stream.WriteByte(0xF9); // Graphic control label
        _stream.WriteByte(4);    // Block size

        // Packed byte:
        // Bits 7-5: Reserved = 0
        // Bits 4-2: Disposal method = 1 (do not dispose - leave frame in place)
        // Bit 1: User input flag = 0
        // Bit 0: Transparent color flag = 0
        // Disposal method 1 means the frame stays visible until the next frame is drawn
        byte packed = (1 << 2); // Disposal method 1
        _stream.WriteByte(packed);

        // Delay time (in hundredths of a second)
        int delay = (int)(frame.Duration.TotalMilliseconds / 10);
        WriteShort(delay);

        // Transparent color index
        _stream.WriteByte(0);

        // Block terminator
        _stream.WriteByte(0);
    }

    private void WriteImageDescriptor(int width, int height)
    {
        _stream.WriteByte(0x2C); // Image separator

        // Image position
        WriteShort(0); // Left
        WriteShort(0); // Top

        // Image size
        WriteShort(width);
        WriteShort(height);

        // Packed byte:
        // Local Color Table Flag = 0 (use global)
        // Interlace Flag = 0
        // Sort Flag = 0
        // Reserved = 0
        // Size of Local Color Table = 0
        _stream.WriteByte(0);
    }

    private void WritePixelData(byte[] indexedPixels)
    {
        var encoder = new LzwEncoder(indexedPixels, ColorDepth);
        encoder.Encode(_stream);
    }

    private void WriteTrailer()
    {
        _stream.WriteByte(0x3B); // GIF trailer
    }

    private static byte[] GetRgbPixels(ImageFrame frame)
    {
        var buffer = frame.Buffer;
        int width = buffer.Width;
        int height = buffer.Height;
        var pixels = new byte[width * height * 3];

        int index = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var pixel = buffer.GetPixel(x, y);
                pixels[index++] = pixel.R;
                pixels[index++] = pixel.G;
                pixels[index++] = pixel.B;
            }
        }

        return pixels;
    }

    private static byte[] GetIndexedPixels(byte[] rgbPixels, NeuQuant quantizer)
    {
        int pixelCount = rgbPixels.Length / 3;
        var indexed = new byte[pixelCount];

        for (int i = 0; i < pixelCount; i++)
        {
            int offset = i * 3;
            indexed[i] = (byte)quantizer.Map(
                rgbPixels[offset],     // R
                rgbPixels[offset + 1], // G
                rgbPixels[offset + 2]  // B
            );
        }

        return indexed;
    }

    private void WriteShort(int value)
    {
        _stream.WriteByte((byte)(value & 0xFF));
        _stream.WriteByte((byte)((value >> 8) & 0xFF));
    }

    private void WriteString(string s)
    {
        foreach (char c in s)
        {
            _stream.WriteByte((byte)c);
        }
    }
}
