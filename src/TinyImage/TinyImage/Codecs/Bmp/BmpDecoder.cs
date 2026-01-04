using System;
using System.Buffers.Binary;
using System.IO;
using TinyImage.Codecs.Jpeg;
using TinyImage.Codecs.Png;

namespace TinyImage.Codecs.Bmp;

/// <summary>
/// Decodes BMP image data from a stream.
/// Supports all bit depths (1, 2, 4, 8, 16, 24, 32, 64) and compression types
/// (RGB, RLE4, RLE8, RLE24, BitFields, AlphaBitFields, OS/2 Huffman).
/// </summary>
internal sealed class BmpDecoder
{
    private readonly Stream _stream;
    private readonly byte[] _buffer;
    private int _bufferPos;
    private int _bufferLength;

    private BmpFileHeader _fileHeader;
    private BmpInfoHeader _infoHeader;
    private Rgba32[]? _palette;
    private BmpColorMasks _colorMasks;

    // Output
    private int _width;
    private int _height;
    private bool _bottomUp;
    private byte[]? _pixels;
    
    // ICC Profile support
    private byte[]? _iccProfile;
    
    /// <summary>
    /// Controls how undefined (skipped) pixels in RLE images are handled.
    /// </summary>
    public enum UndefinedPixelMode
    {
        /// <summary>
        /// Leave as first palette color (index 0) or black.
        /// </summary>
        Leave,
        
        /// <summary>
        /// Make undefined pixels fully transparent.
        /// </summary>
        Transparent
    }
    
    /// <summary>
    /// Gets or sets how undefined pixels in RLE images are handled.
    /// Default is Transparent (adds alpha channel).
    /// </summary>
    public UndefinedPixelMode UndefinedMode { get; set; } = UndefinedPixelMode.Transparent;
    
    /// <summary>
    /// Gets or sets the 64-bit conversion mode.
    /// </summary>
    public Bmp64BitConverter.ConversionMode Conversion64Mode { get; set; } = Bmp64BitConverter.ConversionMode.ToSrgb;
    
    /// <summary>
    /// Gets the embedded ICC profile data, if present (V5 headers only).
    /// </summary>
    public byte[]? IccProfile => _iccProfile;

    public BmpDecoder(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _buffer = new byte[8192];
        _bufferPos = 0;
        _bufferLength = 0;
    }

    /// <summary>
    /// Decodes the BMP image and returns the RGBA pixel data.
    /// </summary>
    public (int width, int height, byte[] pixels, bool hasAlpha) Decode()
    {
        ReadFileHeader();
        
        // For monochrome icons, we already have the image from masks
        if (_isIcon && _iconDecoder != null && _iconDecoder.IsMonochrome && _pixels != null)
        {
            return (_width, _height, _pixels, true); // Icons always have alpha
        }
        
        ReadInfoHeader();
        ReadPalette();
        ReadIccProfile();
        ReadPixelData();
        
        // For color icons, apply the AND mask as alpha channel
        if (_isIcon && _iconDecoder != null && !_iconDecoder.IsMonochrome && _pixels != null)
        {
            _iconDecoder.ApplyAlphaMask(_pixels, _width, _height);
            return (_width, _height, _pixels, true); // Icons always have alpha
        }

        // Determine if image has meaningful alpha:
        // - 32-bit with alpha mask
        // - 64-bit always has alpha
        // - RLE with transparent undefined pixels mode
        // - Embedded PNG with alpha
        bool isRle = _infoHeader.Compression is BmpCompression.RLE4 or BmpCompression.RLE8 or BmpCompression.RLE24;
        bool hasAlpha = (_infoHeader.BitsPerPixel == 32 && _infoHeader.AlphaMask != 0) ||
                        _infoHeader.BitsPerPixel == 64 ||
                        (isRle && UndefinedMode == UndefinedPixelMode.Transparent) ||
                        (_infoHeader.Compression == BmpCompression.PNG && _embeddedHasAlpha);
        
        return (_width, _height, _pixels!, hasAlpha);
    }
    
    private void ReadIccProfile()
    {
        // ICC profile is only in V5 headers with PROFILE_EMBEDDED color space
        if (_infoHeader.HeaderSize < BmpConstants.HeaderSizes.InfoHeaderV5)
            return;
            
        if (_infoHeader.ColorSpaceType != BmpColorSpace.PROFILE_EMBEDDED)
            return;
            
        // Profile data offset is relative to start of BITMAPV5HEADER
        // ProfileData field contains offset from start of header
        // ProfileSize field contains size of profile
        int profileOffset = _infoHeader.ProfileDataOffset;
        int profileSize = _infoHeader.ProfileSize;
        
        if (profileSize <= 0 || profileSize > 1024 * 1024) // Max 1MB profile
            return;
            
        // Calculate absolute position: file header (14) + profile offset
        int absoluteOffset = BmpFileHeader.Size + profileOffset;
        
        // Save current position
        long currentPos = _stream.Position;
        
        try
        {
            _stream.Seek(absoluteOffset, SeekOrigin.Begin);
            _iccProfile = new byte[profileSize];
            int bytesRead = _stream.Read(_iccProfile, 0, profileSize);
            
            if (bytesRead < profileSize)
                _iccProfile = null; // Truncated
        }
        catch
        {
            _iccProfile = null;
        }
        finally
        {
            // Restore position for pixel data reading
            _stream.Seek(currentPos, SeekOrigin.Begin);
            _bufferPos = 0;
            _bufferLength = 0;
        }
    }

    // Icon decoder for OS/2 icons/pointers
    private BmpIconDecoder? _iconDecoder;
    private bool _isIcon;
    
    private void ReadFileHeader()
    {
        Span<byte> headerBytes = stackalloc byte[BmpFileHeader.Size];
        ReadExact(headerBytes);
        _fileHeader = BmpFileHeader.Parse(headerBytes);

        if (!_fileHeader.IsValid)
            throw new InvalidOperationException("Invalid BMP file signature.");

        switch (_fileHeader.Type)
        {
            case BmpConstants.TypeMarkers.Bitmap:
                // Standard single-image BMP, header already parsed
                break;

            case BmpConstants.TypeMarkers.BitmapArray:
                // OS/2 Bitmap Array - skip the array header and read the first bitmap's file header
                // The array header has been read; now read the actual bitmap file header that follows
                ReadExact(headerBytes);
                _fileHeader = BmpFileHeader.Parse(headerBytes);

                if (_fileHeader.Type != BmpConstants.TypeMarkers.Bitmap)
                    throw new NotSupportedException($"Unsupported bitmap type inside BitmapArray: 0x{_fileHeader.Type:X4}");
                break;

            case BmpConstants.TypeMarkers.ColorIcon:
            case BmpConstants.TypeMarkers.ColorPointer:
            case BmpConstants.TypeMarkers.Icon:
            case BmpConstants.TypeMarkers.Pointer:
                _isIcon = true;
                _iconDecoder = new BmpIconDecoder(_stream, _fileHeader.Type);
                HandleIconFormat();
                break;

            default:
                throw new InvalidOperationException($"Unknown BMP file type marker: 0x{_fileHeader.Type:X4}");
        }
    }
    
    private void HandleIconFormat()
    {
        if (_iconDecoder == null)
            return;
            
        // Load the AND/XOR masks
        if (!_iconDecoder.LoadMasks())
            throw new InvalidOperationException("Failed to load icon/pointer masks.");
            
        // For monochrome icons (IC/PT), the masks ARE the image
        // For color icons (CI/CP), we need to read the color image that follows
        if (_iconDecoder.IsMonochrome)
        {
            // Monochrome - create image from masks directly
            var (maskWidth, maskHeight) = _iconDecoder.MaskDimensions;
            _width = maskWidth;
            _height = maskHeight;
            _pixels = _iconDecoder.CreateMonochromeImage();
            _bottomUp = true;
        }
        else
        {
            // Color icon/pointer - need to read the following color image
            // The stream is now positioned at the color image file header
            // We'll re-read the file header from current position
            _bufferPos = 0;
            _bufferLength = 0;
            
            Span<byte> headerBytes = stackalloc byte[BmpFileHeader.Size];
            ReadExact(headerBytes);
            _fileHeader = BmpFileHeader.Parse(headerBytes);
            
            // The color image should be a regular bitmap
            if (_fileHeader.Type != BmpConstants.TypeMarkers.Bitmap &&
                _fileHeader.Type != BmpConstants.TypeMarkers.ColorIcon &&
                _fileHeader.Type != BmpConstants.TypeMarkers.ColorPointer)
            {
                throw new InvalidOperationException($"Expected color image data, got type: 0x{_fileHeader.Type:X4}");
            }
        }
    }

    private void ReadInfoHeader()
    {
        // Read header size first to know how much to read
        Span<byte> sizeBytes = stackalloc byte[4];
        ReadExact(sizeBytes);
        int headerSize = BinaryPrimitives.ReadInt32LittleEndian(sizeBytes);

        // Read the rest of the header
        byte[] headerBytes = new byte[headerSize];
        sizeBytes.CopyTo(headerBytes);
        ReadExact(headerBytes.AsSpan(4));

        _infoHeader = BmpInfoHeader.Parse(headerBytes);
        _width = _infoHeader.Width;
        _height = _infoHeader.AbsoluteHeight;
        _bottomUp = _infoHeader.IsBottomUp;

        // Read color masks if needed (for V3 headers with BitFields compression)
        if (_infoHeader.HeaderSize == BmpConstants.HeaderSizes.InfoHeaderV3)
        {
            if (_infoHeader.Compression == BmpCompression.BitFields ||
                _infoHeader.Compression == BmpCompression.AlphaBitFields)
            {
                Span<byte> maskBytes = stackalloc byte[_infoHeader.Compression == BmpCompression.AlphaBitFields ? 16 : 12];
                ReadExact(maskBytes);

                _infoHeader.RedMask = BinaryPrimitives.ReadUInt32LittleEndian(maskBytes);
                _infoHeader.GreenMask = BinaryPrimitives.ReadUInt32LittleEndian(maskBytes.Slice(4));
                _infoHeader.BlueMask = BinaryPrimitives.ReadUInt32LittleEndian(maskBytes.Slice(8));

                if (_infoHeader.Compression == BmpCompression.AlphaBitFields)
                {
                    _infoHeader.AlphaMask = BinaryPrimitives.ReadUInt32LittleEndian(maskBytes.Slice(12));
                }
            }
        }

        _colorMasks = new BmpColorMasks(_infoHeader);

        ValidateHeader();
    }

    private void ValidateHeader()
    {
        if (_width <= 0 || _height <= 0)
            throw new InvalidOperationException("Invalid image dimensions.");

        if (_infoHeader.BitsPerPixel != 1 && _infoHeader.BitsPerPixel != 2 &&
            _infoHeader.BitsPerPixel != 4 && _infoHeader.BitsPerPixel != 8 &&
            _infoHeader.BitsPerPixel != 16 && _infoHeader.BitsPerPixel != 24 &&
            _infoHeader.BitsPerPixel != 32 && _infoHeader.BitsPerPixel != 64)
        {
            throw new NotSupportedException($"Unsupported bits per pixel: {_infoHeader.BitsPerPixel}");
        }

        // BI_JPEG and BI_PNG are now supported via embedded image decoding
        // They require V4+ headers (108+ byte headers)
        if (_infoHeader.Compression == BmpCompression.JPEG || _infoHeader.Compression == BmpCompression.PNG)
        {
            if (_infoHeader.HeaderSize < BmpConstants.HeaderSizes.InfoHeaderV4)
            {
                throw new InvalidOperationException(
                    $"Embedded JPEG/PNG compression requires BITMAPV4HEADER or later (header size >= 108, got {_infoHeader.HeaderSize}).");
            }
        }
        
        // Validate compression type vs bit depth
        if (_infoHeader.Compression == BmpCompression.OS2Huffman && _infoHeader.BitsPerPixel != 1)
        {
            throw new InvalidOperationException("OS/2 Huffman compression requires 1-bit images.");
        }
        
        if (_infoHeader.Compression == BmpCompression.RLE24 && _infoHeader.BitsPerPixel != 24)
        {
            throw new InvalidOperationException("OS/2 RLE24 compression requires 24-bit images.");
        }
        
        if (_infoHeader.Compression == BmpCompression.RLE4 && _infoHeader.BitsPerPixel != 4)
        {
            throw new InvalidOperationException("RLE4 compression requires 4-bit images.");
        }
        
        if (_infoHeader.Compression == BmpCompression.RLE8 && _infoHeader.BitsPerPixel != 8)
        {
            throw new InvalidOperationException("RLE8 compression requires 8-bit images.");
        }
        
        // 64-bit doesn't support BitFields
        if (_infoHeader.BitsPerPixel == 64 && 
            (_infoHeader.Compression == BmpCompression.BitFields || 
             _infoHeader.Compression == BmpCompression.AlphaBitFields))
        {
            throw new InvalidOperationException("64-bit BMPs don't support BitFields compression.");
        }
    }

    private void ReadPalette()
    {
        int colorCount = _infoHeader.GetPaletteColorCount();
        if (colorCount == 0)
            return;

        int entrySize = _infoHeader.GetPaletteEntrySize();
        int paletteSize = colorCount * entrySize;
        byte[] paletteBytes = new byte[paletteSize];
        ReadExact(paletteBytes);

        _palette = new Rgba32[colorCount];
        for (int i = 0; i < colorCount; i++)
        {
            int offset = i * entrySize;
            byte b = paletteBytes[offset];
            byte g = paletteBytes[offset + 1];
            byte r = paletteBytes[offset + 2];
            // Fourth byte is reserved (quad) in 4-byte entries
            _palette[i] = new Rgba32(r, g, b, 255);
        }
    }

    private void ReadPixelData()
    {
        // Seek to pixel data offset
        SeekTo(_fileHeader.PixelDataOffset);

        // Handle embedded JPEG/PNG - these bypass normal bit-depth decoding
        if (_infoHeader.Compression == BmpCompression.JPEG)
        {
            DecodeEmbeddedJpeg();
            return;
        }
        
        if (_infoHeader.Compression == BmpCompression.PNG)
        {
            DecodeEmbeddedPng();
            return;
        }

        _pixels = new byte[_width * _height * 4];
        
        // For RLE with transparent undefined pixels, initialize to transparent
        bool isRle = _infoHeader.Compression is BmpCompression.RLE4 or BmpCompression.RLE8 or BmpCompression.RLE24;
        if (isRle && UndefinedMode == UndefinedPixelMode.Transparent)
        {
            // Already zeroed (transparent black)
        }

        switch (_infoHeader.BitsPerPixel)
        {
            case 1:
                if (_infoHeader.Compression == BmpCompression.OS2Huffman)
                    DecodeHuffman();
                else
                    DecodeBit1();
                break;
            case 2:
                DecodeBit2();
                break;
            case 4:
                if (_infoHeader.Compression == BmpCompression.RLE4)
                    DecodeRle4();
                else
                    DecodeBit4();
                break;
            case 8:
                if (_infoHeader.Compression == BmpCompression.RLE8)
                    DecodeRle8();
                else
                    DecodeBit8();
                break;
            case 16:
                DecodeBit16();
                break;
            case 24:
                if (_infoHeader.Compression == BmpCompression.RLE24)
                    DecodeRle24();
                else
                    DecodeBit24();
                break;
            case 32:
                DecodeBit32();
                break;
            case 64:
                DecodeBit64();
                break;
        }
    }

    private void DecodeBit1()
    {
        int bytesPerRow = (_width + 7) / 8;
        int padding = (4 - (bytesPerRow % 4)) % 4;
        byte[] rowBuffer = new byte[bytesPerRow + padding];

        for (int y = 0; y < _height; y++)
        {
            ReadExact(rowBuffer.AsSpan(0, bytesPerRow + padding));
            int destY = _bottomUp ? (_height - 1 - y) : y;

            for (int x = 0; x < _width; x++)
            {
                int byteIndex = x / 8;
                int bitIndex = 7 - (x % 8);
                int colorIndex = (rowBuffer[byteIndex] >> bitIndex) & 0x01;

                SetPixel(x, destY, _palette![colorIndex]);
            }
        }
    }

    private void DecodeBit2()
    {
        int bytesPerRow = (_width + 3) / 4;
        int padding = (4 - (bytesPerRow % 4)) % 4;
        byte[] rowBuffer = new byte[bytesPerRow + padding];

        for (int y = 0; y < _height; y++)
        {
            ReadExact(rowBuffer.AsSpan(0, bytesPerRow + padding));
            int destY = _bottomUp ? (_height - 1 - y) : y;

            for (int x = 0; x < _width; x++)
            {
                int byteIndex = x / 4;
                int shift = (3 - (x % 4)) * 2;
                int colorIndex = (rowBuffer[byteIndex] >> shift) & 0x03;

                SetPixel(x, destY, _palette![colorIndex]);
            }
        }
    }

    private void DecodeBit4()
    {
        int bytesPerRow = (_width + 1) / 2;
        int padding = (4 - (bytesPerRow % 4)) % 4;
        byte[] rowBuffer = new byte[bytesPerRow + padding];

        for (int y = 0; y < _height; y++)
        {
            ReadExact(rowBuffer.AsSpan(0, bytesPerRow + padding));
            int destY = _bottomUp ? (_height - 1 - y) : y;

            for (int x = 0; x < _width; x++)
            {
                int byteIndex = x / 2;
                int colorIndex;
                if ((x & 1) == 0)
                    colorIndex = (rowBuffer[byteIndex] >> 4) & 0x0F;
                else
                    colorIndex = rowBuffer[byteIndex] & 0x0F;

                SetPixel(x, destY, _palette![colorIndex]);
            }
        }
    }

    private void DecodeBit8()
    {
        int bytesPerRow = _width;
        int padding = (4 - (bytesPerRow % 4)) % 4;
        byte[] rowBuffer = new byte[bytesPerRow + padding];

        for (int y = 0; y < _height; y++)
        {
            ReadExact(rowBuffer.AsSpan(0, bytesPerRow + padding));
            int destY = _bottomUp ? (_height - 1 - y) : y;

            for (int x = 0; x < _width; x++)
            {
                int colorIndex = rowBuffer[x];
                if (colorIndex < _palette!.Length)
                    SetPixel(x, destY, _palette[colorIndex]);
                else
                    SetPixel(x, destY, new Rgba32(0, 0, 0, 255));
            }
        }
    }

    private void DecodeRle4()
    {
        // Initialize to black/transparent
        Array.Clear(_pixels!, 0, _pixels!.Length);

        int x = 0;
        int y = _bottomUp ? _height - 1 : 0;
        bool lowNibble = false;

        while (true)
        {
            byte first = ReadByte();
            byte second = ReadByte();

            if (first == 0)
            {
                // Escape codes
                switch (second)
                {
                    case 0: // End of line
                        x = 0;
                        y += _bottomUp ? -1 : 1;
                        lowNibble = false;
                        if (y < 0 || y >= _height)
                            return;
                        break;

                    case 1: // End of bitmap
                        return;

                    case 2: // Delta
                        int dx = ReadByte();
                        int dy = ReadByte();
                        x += dx;
                        y += _bottomUp ? -dy : dy;
                        break;

                    default: // Absolute mode
                        int count = second;
                        for (int i = 0; i < count; i++)
                        {
                            int colorIndex;
                            if ((i & 1) == 0)
                            {
                                byte dataByte = ReadByte();
                                colorIndex = (dataByte >> 4) & 0x0F;
                                // Save low nibble for next iteration
                                if (i + 1 < count)
                                {
                                    i++;
                                    if (x < _width && y >= 0 && y < _height)
                                        SetPixel(x++, y, _palette![colorIndex]);
                                    colorIndex = dataByte & 0x0F;
                                }
                            }
                            else
                            {
                                continue; // Already processed
                            }

                            if (x < _width && y >= 0 && y < _height)
                                SetPixel(x++, y, _palette![colorIndex]);
                        }
                        // Align to word boundary
                        if ((((count + 1) / 2) & 1) == 1)
                            ReadByte();
                        break;
                }
            }
            else
            {
                // Encoded mode: repeat nibbles 'first' times
                int highNibble = (second >> 4) & 0x0F;
                int lowerNibble = second & 0x0F;

                for (int i = 0; i < first; i++)
                {
                    int colorIndex = (i & 1) == 0 ? highNibble : lowerNibble;
                    if (x < _width && y >= 0 && y < _height)
                        SetPixel(x++, y, _palette![colorIndex]);
                }
            }
        }
    }

    private void DecodeRle8()
    {
        // Initialize to black/transparent
        Array.Clear(_pixels!, 0, _pixels!.Length);

        int x = 0;
        int y = _bottomUp ? _height - 1 : 0;

        while (true)
        {
            byte first = ReadByte();
            byte second = ReadByte();

            if (first == 0)
            {
                // Escape codes
                switch (second)
                {
                    case 0: // End of line
                        x = 0;
                        y += _bottomUp ? -1 : 1;
                        if (y < 0 || y >= _height)
                            return;
                        break;

                    case 1: // End of bitmap
                        return;

                    case 2: // Delta
                        int dx = ReadByte();
                        int dy = ReadByte();
                        x += dx;
                        y += _bottomUp ? -dy : dy;
                        break;

                    default: // Absolute mode
                        int count = second;
                        for (int i = 0; i < count; i++)
                        {
                            byte colorIndex = ReadByte();
                            if (x < _width && y >= 0 && y < _height && colorIndex < _palette!.Length)
                                SetPixel(x++, y, _palette[colorIndex]);
                            else
                                x++;
                        }
                        // Align to word boundary
                        if ((count & 1) == 1)
                            ReadByte();
                        break;
                }
            }
            else
            {
                // Encoded mode: repeat 'second' color 'first' times
                if (second < _palette!.Length)
                {
                    var color = _palette[second];
                    for (int i = 0; i < first; i++)
                    {
                        if (x < _width && y >= 0 && y < _height)
                            SetPixel(x++, y, color);
                        else
                            x++;
                    }
                }
                else
                {
                    x += first;
                }
            }
        }
    }

    private void DecodeBit16()
    {
        int bytesPerRow = _width * 2;
        int padding = (4 - (bytesPerRow % 4)) % 4;
        byte[] rowBuffer = new byte[bytesPerRow + padding];

        for (int y = 0; y < _height; y++)
        {
            ReadExact(rowBuffer.AsSpan(0, bytesPerRow + padding));
            int destY = _bottomUp ? (_height - 1 - y) : y;

            for (int x = 0; x < _width; x++)
            {
                int offset = x * 2;
                ushort pixel = BinaryPrimitives.ReadUInt16LittleEndian(rowBuffer.AsSpan(offset));

                _colorMasks.ExtractRgba(pixel, out byte r, out byte g, out byte b, out byte a);
                SetPixel(x, destY, new Rgba32(r, g, b, a));
            }
        }
    }

    private void DecodeBit24()
    {
        int bytesPerRow = _width * 3;
        int padding = (4 - (bytesPerRow % 4)) % 4;
        byte[] rowBuffer = new byte[bytesPerRow + padding];

        for (int y = 0; y < _height; y++)
        {
            ReadExact(rowBuffer.AsSpan(0, bytesPerRow + padding));
            int destY = _bottomUp ? (_height - 1 - y) : y;

            for (int x = 0; x < _width; x++)
            {
                int offset = x * 3;
                byte b = rowBuffer[offset];
                byte g = rowBuffer[offset + 1];
                byte r = rowBuffer[offset + 2];

                SetPixel(x, destY, new Rgba32(r, g, b, 255));
            }
        }
    }

    private void DecodeBit32()
    {
        byte[] rowBuffer = new byte[_width * 4];

        for (int y = 0; y < _height; y++)
        {
            ReadExact(rowBuffer);
            int destY = _bottomUp ? (_height - 1 - y) : y;

            for (int x = 0; x < _width; x++)
            {
                int offset = x * 4;
                uint pixel = BinaryPrimitives.ReadUInt32LittleEndian(rowBuffer.AsSpan(offset));

                _colorMasks.ExtractRgba(pixel, out byte r, out byte g, out byte b, out byte a);
                SetPixel(x, destY, new Rgba32(r, g, b, a));
            }
        }
    }

    private void DecodeBit64()
    {
        // 64-bit BMPs: 16-bit per channel RGBA in s2.13 fixed-point format, linear light
        // Format: BGRA (blue first, then green, red, alpha)
        byte[] rowBuffer = new byte[_width * 8];

        for (int y = 0; y < _height; y++)
        {
            ReadExact(rowBuffer);
            int destY = _bottomUp ? (_height - 1 - y) : y;

            for (int x = 0; x < _width; x++)
            {
                int offset = x * 8;
                ushort b = BinaryPrimitives.ReadUInt16LittleEndian(rowBuffer.AsSpan(offset));
                ushort g = BinaryPrimitives.ReadUInt16LittleEndian(rowBuffer.AsSpan(offset + 2));
                ushort r = BinaryPrimitives.ReadUInt16LittleEndian(rowBuffer.AsSpan(offset + 4));
                ushort a = BinaryPrimitives.ReadUInt16LittleEndian(rowBuffer.AsSpan(offset + 6));

                var color = Bmp64BitConverter.ConvertPixel(b, g, r, a, Conversion64Mode);
                SetPixel(x, destY, color);
            }
        }
    }

    private void DecodeHuffman()
    {
        // OS/2 1-bit Huffman (ITU-T T.4/G3 fax) compression
        var decoder = new BmpHuffmanDecoder(_stream);
        byte[] lineBuffer = new byte[_width];

        for (int y = 0; y < _height; y++)
        {
            int destY = _bottomUp ? (_height - 1 - y) : y;

            if (!decoder.DecodeLine(lineBuffer, _width, blackIsZero: false))
            {
                // Truncated - leave remaining pixels as-is
                break;
            }

            for (int x = 0; x < _width; x++)
            {
                int colorIndex = lineBuffer[x] == 0 ? 1 : 0;
                if (_palette != null && colorIndex < _palette.Length)
                    SetPixel(x, destY, _palette[colorIndex]);
                else
                    SetPixel(x, destY, lineBuffer[x] == 0 ? new Rgba32(0, 0, 0, 255) : new Rgba32(255, 255, 255, 255));
            }
        }
    }

    private void DecodeRle24()
    {
        // OS/2 24-bit RLE compression
        // Similar to RLE8 but with 3-byte RGB values instead of palette indices
        int x = 0;
        int y = _bottomUp ? _height - 1 : 0;
        bool transparentUndefined = UndefinedMode == UndefinedPixelMode.Transparent;

        while (true)
        {
            byte first = ReadByte();
            byte second = ReadByte();

            if (first == 0)
            {
                // Escape codes
                switch (second)
                {
                    case 0: // End of line
                        x = 0;
                        y += _bottomUp ? -1 : 1;
                        if (y < 0 || y >= _height)
                            return;
                        break;

                    case 1: // End of bitmap
                        return;

                    case 2: // Delta
                        int dx = ReadByte();
                        int dy = ReadByte();
                        // Pixels skipped by delta are undefined
                        // If transparent mode, they stay as initialized (transparent)
                        x += dx;
                        y += _bottomUp ? -dy : dy;
                        break;

                    default: // Absolute mode - second is count of literal RGB triplets
                        int count = second;
                        for (int i = 0; i < count; i++)
                        {
                            byte b = ReadByte();
                            byte g = ReadByte();
                            byte r = ReadByte();
                            
                            if (x < _width && y >= 0 && y < _height)
                                SetPixel(x++, y, new Rgba32(r, g, b, 255));
                            else
                                x++;
                        }
                        // Align to word boundary (count * 3 bytes)
                        if ((count * 3) % 2 == 1)
                            ReadByte();
                        break;
                }
            }
            else
            {
                // Encoded mode: repeat RGB triplet 'first' times
                byte b = second;
                byte g = ReadByte();
                byte r = ReadByte();
                var color = new Rgba32(r, g, b, 255);

                for (int i = 0; i < first; i++)
                {
                    if (x < _width && y >= 0 && y < _height)
                        SetPixel(x++, y, color);
                    else
                        x++;
                }
            }
        }
    }

    private void DecodeEmbeddedJpeg()
    {
        // BI_JPEG: The pixel data section contains an embedded JPEG image.
        // Used primarily for printing purposes in V4+ headers.
        // ImageSize field contains the size of the JPEG data.
        int jpegSize = _infoHeader.ImageSize;
        if (jpegSize <= 0)
        {
            // Try to calculate from file size if ImageSize is 0
            jpegSize = _fileHeader.FileSize - _fileHeader.PixelDataOffset;
        }
        
        if (jpegSize <= 0)
            throw new InvalidOperationException("Cannot determine embedded JPEG data size.");
        
        // Read the embedded JPEG data
        byte[] jpegData = new byte[jpegSize];
        int bytesRead = 0;
        while (bytesRead < jpegSize)
        {
            int read = _stream.Read(jpegData, bytesRead, jpegSize - bytesRead);
            if (read == 0)
                break; // EOF - use what we have
            bytesRead += read;
        }
        
        // Decode using JPEG codec
        using var jpegStream = new MemoryStream(jpegData, 0, bytesRead);
        var image = JpegCodec.Decode(jpegStream);
        
        // Verify dimensions match (or use JPEG dimensions if BMP header has placeholder values)
        var buffer = image.GetBuffer();
        if (_width == 0 || _height == 0)
        {
            _width = image.Width;
            _height = image.Height;
        }
        
        // Copy pixels to output buffer
        _pixels = new byte[_width * _height * 4];
        var rawData = buffer.GetRawData();
        
        // JPEG doesn't have alpha, copy RGB and set alpha to 255
        for (int y = 0; y < Math.Min(_height, image.Height); y++)
        {
            for (int x = 0; x < Math.Min(_width, image.Width); x++)
            {
                int srcOffset = (y * image.Width + x) * 4;
                int destOffset = (y * _width + x) * 4;
                
                _pixels[destOffset] = rawData[srcOffset];     // R
                _pixels[destOffset + 1] = rawData[srcOffset + 1]; // G
                _pixels[destOffset + 2] = rawData[srcOffset + 2]; // B
                _pixels[destOffset + 3] = 255;                 // A (JPEG has no alpha)
            }
        }
    }

    private void DecodeEmbeddedPng()
    {
        // BI_PNG: The pixel data section contains an embedded PNG image.
        // Used primarily for printing purposes in V4+ headers.
        // ImageSize field contains the size of the PNG data.
        int pngSize = _infoHeader.ImageSize;
        if (pngSize <= 0)
        {
            // Try to calculate from file size if ImageSize is 0
            pngSize = _fileHeader.FileSize - _fileHeader.PixelDataOffset;
        }
        
        if (pngSize <= 0)
            throw new InvalidOperationException("Cannot determine embedded PNG data size.");
        
        // Read the embedded PNG data
        byte[] pngData = new byte[pngSize];
        int bytesRead = 0;
        while (bytesRead < pngSize)
        {
            int read = _stream.Read(pngData, bytesRead, pngSize - bytesRead);
            if (read == 0)
                break; // EOF - use what we have
            bytesRead += read;
        }
        
        // Decode using PNG codec
        using var pngStream = new MemoryStream(pngData, 0, bytesRead);
        var image = PngCodec.Decode(pngStream);
        
        // Verify dimensions match (or use PNG dimensions if BMP header has placeholder values)
        var buffer = image.GetBuffer();
        if (_width == 0 || _height == 0)
        {
            _width = image.Width;
            _height = image.Height;
        }
        
        // Copy pixels to output buffer
        _pixels = new byte[_width * _height * 4];
        var rawData = buffer.GetRawData();
        
        // PNG may have alpha, copy all 4 channels
        for (int y = 0; y < Math.Min(_height, image.Height); y++)
        {
            for (int x = 0; x < Math.Min(_width, image.Width); x++)
            {
                int srcOffset = (y * image.Width + x) * 4;
                int destOffset = (y * _width + x) * 4;
                
                _pixels[destOffset] = rawData[srcOffset];     // R
                _pixels[destOffset + 1] = rawData[srcOffset + 1]; // G
                _pixels[destOffset + 2] = rawData[srcOffset + 2]; // B
                _pixels[destOffset + 3] = rawData[srcOffset + 3]; // A
            }
        }
        
        // Track if PNG has alpha for the return value
        _embeddedHasAlpha = image.HasAlpha;
    }
    
    // Flag to track if embedded PNG has alpha
    private bool _embeddedHasAlpha;

    private void SetPixel(int x, int y, Rgba32 color)
    {
        int offset = (y * _width + x) * 4;
        _pixels![offset] = color.R;
        _pixels![offset + 1] = color.G;
        _pixels![offset + 2] = color.B;
        _pixels![offset + 3] = color.A;
    }

    private void ReadExact(Span<byte> buffer)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            // Refill buffer if needed
            if (_bufferPos >= _bufferLength)
            {
                _bufferLength = _stream.Read(_buffer, 0, _buffer.Length);
                _bufferPos = 0;
                if (_bufferLength == 0)
                    throw new EndOfStreamException("Unexpected end of BMP data.");
            }

            int available = _bufferLength - _bufferPos;
            int needed = buffer.Length - totalRead;
            int toCopy = Math.Min(available, needed);

            _buffer.AsSpan(_bufferPos, toCopy).CopyTo(buffer.Slice(totalRead));
            _bufferPos += toCopy;
            totalRead += toCopy;
        }
    }

    private byte ReadByte()
    {
        if (_bufferPos >= _bufferLength)
        {
            _bufferLength = _stream.Read(_buffer, 0, _buffer.Length);
            _bufferPos = 0;
            if (_bufferLength == 0)
                throw new EndOfStreamException("Unexpected end of BMP data.");
        }
        return _buffer[_bufferPos++];
    }

    private void SeekTo(int position)
    {
        if (_stream.CanSeek)
        {
            _stream.Seek(position, SeekOrigin.Begin);
            _bufferPos = 0;
            _bufferLength = 0;
        }
        else
        {
            // For non-seekable streams, we need to calculate current position
            // This is a simplified approach - in practice, we track position
            throw new NotSupportedException("Stream must be seekable for BMP decoding.");
        }
    }
}
