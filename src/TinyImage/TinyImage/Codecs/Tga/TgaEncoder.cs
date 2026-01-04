using System;
using System.IO;

namespace TinyImage.Codecs.Tga;

/// <summary>
/// Encodes image data to TGA format.
/// Supports 24-bit and 32-bit output with optional RLE compression.
/// </summary>
internal sealed class TgaEncoder
{
    private readonly Stream _stream;
    private readonly int _width;
    private readonly int _height;
    private readonly byte[] _pixels;
    private readonly TgaBitsPerPixel _bitsPerPixel;
    private readonly bool _useRle;
    private readonly bool _hasAlpha;

    // TGA v2 footer constants
    private static readonly byte[] TgaSignature = 
    {
        (byte)'T', (byte)'R', (byte)'U', (byte)'E', (byte)'V', (byte)'I', (byte)'S', (byte)'I',
        (byte)'O', (byte)'N', (byte)'-', (byte)'X', (byte)'F', (byte)'I', (byte)'L', (byte)'E',
        (byte)'.', 0x00
    };

    private const int ExtensionAreaSize = 495;
    private const int FooterSize = 26;

    // Extension area attribute types
    private const byte AttrTypeNoAlpha = 0;
    private const byte AttrTypeUndefinedAlpha = 1;
    private const byte AttrTypeRetainedAlpha = 2;
    private const byte AttrTypeAlpha = 3;
    private const byte AttrTypePremultipliedAlpha = 4;

    public TgaEncoder(Stream stream, int width, int height, byte[] pixels, TgaBitsPerPixel bitsPerPixel, bool useRle, bool hasAlpha)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _width = width;
        _height = height;
        _pixels = pixels ?? throw new ArgumentNullException(nameof(pixels));
        _bitsPerPixel = bitsPerPixel;
        _useRle = useRle;
        _hasAlpha = hasAlpha;
    }

    /// <summary>
    /// Encodes the image to the output stream.
    /// </summary>
    public void Encode()
    {
        WriteHeader();
        WritePixelData();
        WriteExtensionArea();
        WriteFooter();
    }

    private void WriteHeader()
    {
        var header = TgaHeader.CreateForEncoding(_width, _height, _bitsPerPixel, _useRle);

        byte[] headerBytes = new byte[TgaHeader.Size];
        header.WriteTo(headerBytes);
        _stream.Write(headerBytes, 0, headerBytes.Length);
    }

    private void WritePixelData()
    {
        if (_useRle)
        {
            WritePixelDataRle();
        }
        else
        {
            WritePixelDataUncompressed();
        }
    }

    private void WritePixelDataUncompressed()
    {
        int bytesPerPixel = (int)_bitsPerPixel / 8;
        byte[] rowBuffer = new byte[_width * bytesPerPixel];

        // Write rows top-to-bottom (top-left origin is set in header)
        for (int y = 0; y < _height; y++)
        {
            EncodeRow(y, rowBuffer);
            _stream.Write(rowBuffer, 0, rowBuffer.Length);
        }
    }

    private void WritePixelDataRle()
    {
        int bytesPerPixel = (int)_bitsPerPixel / 8;

        // Encode each row separately to avoid runs spanning rows
        for (int y = 0; y < _height; y++)
        {
            EncodeRowRle(y, bytesPerPixel);
        }
    }

    private void EncodeRow(int y, byte[] rowBuffer)
    {
        int bytesPerPixel = (int)_bitsPerPixel / 8;

        for (int x = 0; x < _width; x++)
        {
            int srcOffset = (y * _width + x) * 4;
            int dstOffset = x * bytesPerPixel;

            // Convert from RGBA to BGR(A)
            rowBuffer[dstOffset] = _pixels[srcOffset + 2];     // B
            rowBuffer[dstOffset + 1] = _pixels[srcOffset + 1]; // G
            rowBuffer[dstOffset + 2] = _pixels[srcOffset];     // R

            if (bytesPerPixel == 4)
            {
                rowBuffer[dstOffset + 3] = _pixels[srcOffset + 3]; // A
            }
        }
    }

    private void EncodeRowRle(int y, int bytesPerPixel)
    {
        // Maximum buffer size: worst case is all raw packets
        // Each pixel could need (1 header + bytesPerPixel data) bytes
        byte[] buffer = new byte[_width * (bytesPerPixel + 1)];
        int bufferPos = 0;

        int x = 0;
        while (x < _width)
        {
            // Find the run length starting at x
            int runStart = x;
            int runLength = 1;

            // Check if next pixels are the same (RLE candidate)
            while (x + runLength < _width && runLength < 128)
            {
                if (PixelsEqual(y, x, y, x + runLength, bytesPerPixel))
                {
                    runLength++;
                }
                else
                {
                    break;
                }
            }

            if (runLength > 1)
            {
                // RLE packet: repeat single pixel
                buffer[bufferPos++] = (byte)(0x80 | (runLength - 1));
                WritePixelToBuffer(y, x, buffer, ref bufferPos, bytesPerPixel);
                x += runLength;
            }
            else
            {
                // Start a raw packet - find how many different pixels in a row
                int rawStart = x;
                int rawLength = 1;

                while (x + rawLength < _width && rawLength < 128)
                {
                    // Check if next pixel would start a worthwhile RLE run
                    if (x + rawLength + 1 < _width &&
                        PixelsEqual(y, x + rawLength, y, x + rawLength + 1, bytesPerPixel))
                    {
                        // Stop raw packet here, let RLE handle the next run
                        break;
                    }
                    rawLength++;
                }

                // Write raw packet header
                buffer[bufferPos++] = (byte)(rawLength - 1);

                // Write all raw pixels
                for (int i = 0; i < rawLength; i++)
                {
                    WritePixelToBuffer(y, x + i, buffer, ref bufferPos, bytesPerPixel);
                }

                x += rawLength;
            }
        }

        // Write the buffer to stream
        _stream.Write(buffer, 0, bufferPos);
    }

    private bool PixelsEqual(int y1, int x1, int y2, int x2, int bytesPerPixel)
    {
        int offset1 = (y1 * _width + x1) * 4;
        int offset2 = (y2 * _width + x2) * 4;

        // Compare BGR(A) values
        if (_pixels[offset1 + 2] != _pixels[offset2 + 2]) return false; // B
        if (_pixels[offset1 + 1] != _pixels[offset2 + 1]) return false; // G
        if (_pixels[offset1] != _pixels[offset2]) return false;         // R

        if (bytesPerPixel == 4)
        {
            if (_pixels[offset1 + 3] != _pixels[offset2 + 3]) return false; // A
        }

        return true;
    }

    private void WritePixelToBuffer(int y, int x, byte[] buffer, ref int pos, int bytesPerPixel)
    {
        int srcOffset = (y * _width + x) * 4;

        // Convert from RGBA to BGR(A)
        buffer[pos++] = _pixels[srcOffset + 2]; // B
        buffer[pos++] = _pixels[srcOffset + 1]; // G
        buffer[pos++] = _pixels[srcOffset];     // R

        if (bytesPerPixel == 4)
        {
            buffer[pos++] = _pixels[srcOffset + 3]; // A
        }
    }

    private void WriteExtensionArea()
    {
        // Write a minimal extension area to specify the alpha channel type
        byte[] extArea = new byte[ExtensionAreaSize];

        // Extension size (2 bytes) - 495 for TGA 2.0
        extArea[0] = (byte)(ExtensionAreaSize & 0xFF);
        extArea[1] = (byte)((ExtensionAreaSize >> 8) & 0xFF);

        // Author name (41 bytes) - leave empty (bytes 2-42)
        // Author comments (324 bytes) - leave empty (bytes 43-366)
        // Date/time stamp (12 bytes) - leave as zeros (bytes 367-378)
        // Job name (41 bytes) - leave empty (bytes 379-419)
        // Job time (6 bytes) - leave as zeros (bytes 420-425)
        // Software ID (41 bytes) - optionally fill with "TinyImage" (bytes 426-466)
        byte[] softwareId = System.Text.Encoding.ASCII.GetBytes("TinyImage");
        Array.Copy(softwareId, 0, extArea, 426, Math.Min(softwareId.Length, 40));

        // Software version (3 bytes) - leave as zeros (bytes 467-469)
        // Key color (4 bytes) - leave as zeros (bytes 470-473)
        // Pixel aspect ratio (4 bytes) - leave as zeros (bytes 474-477)
        // Gamma value (4 bytes) - leave as zeros (bytes 478-481)
        // Color correction offset (4 bytes) - zero means none (bytes 482-485)
        // Postage stamp offset (4 bytes) - zero means none (bytes 486-489)
        // Scan line offset (4 bytes) - zero means none (bytes 490-493)

        // Attributes type (1 byte) - byte 494
        if (_bitsPerPixel == TgaBitsPerPixel.Bit32 && _hasAlpha)
        {
            extArea[494] = AttrTypeAlpha; // Useful alpha data
        }
        else
        {
            extArea[494] = AttrTypeNoAlpha; // No alpha
        }

        _stream.Write(extArea, 0, extArea.Length);
    }

    private void WriteFooter()
    {
        byte[] footer = new byte[FooterSize];

        // Calculate extension area offset
        // Header (18) + pixel data size
        int bytesPerPixel = (int)_bitsPerPixel / 8;
        long pixelDataSize = _stream.Position - TgaHeader.Size;
        uint extOffset = (uint)(TgaHeader.Size + pixelDataSize);

        // But we need to account that extension area was already written
        // So the offset points to where extension area starts
        extOffset = (uint)(TgaHeader.Size + pixelDataSize - ExtensionAreaSize);

        // Wait, let me recalculate. The stream position after WriteExtensionArea
        // is at the end of extension area. Extension area offset should point
        // to the start of extension area, which is stream.Position - ExtensionAreaSize
        extOffset = (uint)(_stream.Position - ExtensionAreaSize);

        // Extension Area Offset (4 bytes)
        footer[0] = (byte)(extOffset & 0xFF);
        footer[1] = (byte)((extOffset >> 8) & 0xFF);
        footer[2] = (byte)((extOffset >> 16) & 0xFF);
        footer[3] = (byte)((extOffset >> 24) & 0xFF);

        // Developer Directory Offset (4 bytes) - zero (none)
        footer[4] = 0;
        footer[5] = 0;
        footer[6] = 0;
        footer[7] = 0;

        // Signature (18 bytes)
        Array.Copy(TgaSignature, 0, footer, 8, TgaSignature.Length);

        _stream.Write(footer, 0, footer.Length);
    }
}
