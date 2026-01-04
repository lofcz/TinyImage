using System;
using System.Collections.Generic;
using System.IO;
using TinyImage.Codecs.WebP.Lossless;
using TinyImage.Codecs.WebP.Lossy;

namespace TinyImage.Codecs.WebP;

/// <summary>
/// WebP image decoder supporting lossy (VP8), lossless (VP8L), and extended (VP8X) formats.
/// Includes support for alpha channels and animations.
/// </summary>
internal class WebPDecoder
{
    private readonly Stream _stream;
    private int _width;
    private int _height;
    private bool _hasAlpha;
    private bool _isLossy;
    private bool _isAnimated;
    private int _loopCount;
    private WebPExtendedInfo _extendedInfo;

    private readonly List<WebPFrameInfo> _frames = new();
    private byte[] _backgroundColor;

    public WebPDecoder(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    public int Width => _width;
    public int Height => _height;
    public bool HasAlpha => _hasAlpha;
    public bool IsAnimated => _isAnimated;
    public int LoopCount => _loopCount;

    /// <summary>
    /// Decodes the WebP image and returns an Image object.
    /// </summary>
    public Image Decode()
    {
        ReadHeader();

        if (_isAnimated)
        {
            return DecodeAnimated();
        }
        else
        {
            return DecodeSingleFrame();
        }
    }

    private void ReadHeader()
    {
        // Read RIFF header
        byte[] riffTag = new byte[4];
        if (_stream.Read(riffTag, 0, 4) != 4)
            throw new WebPDecodingException("Failed to read RIFF header");

        if (riffTag[0] != 'R' || riffTag[1] != 'I' || riffTag[2] != 'F' || riffTag[3] != 'F')
            throw new WebPDecodingException("Invalid RIFF signature");

        // Read file size
        byte[] sizeBytes = new byte[4];
        if (_stream.Read(sizeBytes, 0, 4) != 4)
            throw new WebPDecodingException("Failed to read file size");
        uint fileSize = BitConverter.ToUInt32(sizeBytes, 0);

        // Read WEBP tag
        byte[] webpTag = new byte[4];
        if (_stream.Read(webpTag, 0, 4) != 4)
            throw new WebPDecodingException("Failed to read WEBP signature");

        if (webpTag[0] != 'W' || webpTag[1] != 'E' || webpTag[2] != 'B' || webpTag[3] != 'P')
            throw new WebPDecodingException("Invalid WEBP signature");

        // Read first chunk
        var chunk = ReadChunkHeader();

        switch (chunk.Type)
        {
            case WebPChunkType.VP8:
                ReadVP8Header(chunk);
                _isLossy = true;
                break;

            case WebPChunkType.VP8L:
                ReadVP8LHeader(chunk);
                _isLossy = false;
                break;

            case WebPChunkType.VP8X:
                ReadVP8XHeader(chunk, fileSize);
                break;

            default:
                throw new WebPDecodingException($"Unexpected chunk type: {chunk.Type}");
        }
    }

    private WebPChunk ReadChunkHeader()
    {
        byte[] fourCC = new byte[4];
        if (_stream.Read(fourCC, 0, 4) != 4)
            throw new WebPDecodingException("Failed to read chunk FourCC");

        byte[] sizeBytes = new byte[4];
        if (_stream.Read(sizeBytes, 0, 4) != 4)
            throw new WebPDecodingException("Failed to read chunk size");

        uint size = BitConverter.ToUInt32(sizeBytes, 0);
        var type = WebPChunk.ParseFourCC(fourCC);

        return new WebPChunk(type, size, fourCC);
    }

    private void ReadVP8Header(WebPChunk chunk)
    {
        long dataStart = _stream.Position;

        // Read frame tag
        byte[] tag = new byte[3];
        if (_stream.Read(tag, 0, 3) != 3)
            throw new WebPDecodingException("Failed to read VP8 frame tag");

        uint tagValue = (uint)(tag[0] | (tag[1] << 8) | (tag[2] << 16));
        bool keyframe = (tagValue & 1) == 0;

        if (!keyframe)
            throw new WebPDecodingException("Non-keyframe VP8 not supported");

        // Read VP8 magic
        byte[] magic = new byte[3];
        if (_stream.Read(magic, 0, 3) != 3)
            throw new WebPDecodingException("Failed to read VP8 magic");

        if (magic[0] != 0x9d || magic[1] != 0x01 || magic[2] != 0x2a)
            throw new WebPDecodingException("Invalid VP8 magic");

        // Read dimensions
        byte[] dimBytes = new byte[4];
        if (_stream.Read(dimBytes, 0, 4) != 4)
            throw new WebPDecodingException("Failed to read VP8 dimensions");

        _width = (dimBytes[0] | (dimBytes[1] << 8)) & 0x3FFF;
        _height = (dimBytes[2] | (dimBytes[3] << 8)) & 0x3FFF;

        // Seek back to chunk data start
        _stream.Position = dataStart;

        // Add as single frame
        _frames.Add(new WebPFrameInfo
        {
            DataStart = dataStart,
            DataSize = chunk.Size,
            IsLossy = true,
            Width = _width,
            Height = _height,
            OffsetX = 0,
            OffsetY = 0,
            Duration = 0
        });
    }

    private void ReadVP8LHeader(WebPChunk chunk)
    {
        long dataStart = _stream.Position;

        // Read signature
        int signature = _stream.ReadByte();
        if (signature != 0x2f)
            throw new WebPDecodingException($"Invalid VP8L signature: 0x{signature:X2}");

        // Read header
        byte[] headerBytes = new byte[4];
        if (_stream.Read(headerBytes, 0, 4) != 4)
            throw new WebPDecodingException("Failed to read VP8L header");

        uint header = BitConverter.ToUInt32(headerBytes, 0);
        _width = (int)((header & 0x3FFF) + 1);
        _height = (int)(((header >> 14) & 0x3FFF) + 1);
        _hasAlpha = ((header >> 28) & 1) != 0;

        uint version = header >> 29;
        if (version != 0)
            throw new WebPDecodingException($"Invalid VP8L version: {version}");

        // Seek back to chunk data start
        _stream.Position = dataStart;

        // Add as single frame
        _frames.Add(new WebPFrameInfo
        {
            DataStart = dataStart,
            DataSize = chunk.Size,
            IsLossy = false,
            Width = _width,
            Height = _height,
            OffsetX = 0,
            OffsetY = 0,
            Duration = 0
        });
    }

    private void ReadVP8XHeader(WebPChunk chunk, uint fileSize)
    {
        // Read VP8X flags
        byte[] flags = new byte[4];
        if (_stream.Read(flags, 0, 4) != 4)
            throw new WebPDecodingException("Failed to read VP8X flags");

        _extendedInfo = new WebPExtendedInfo
        {
            HasIccProfile = (flags[0] & 0x20) != 0,
            HasAlpha = (flags[0] & 0x10) != 0,
            HasExifMetadata = (flags[0] & 0x08) != 0,
            HasXmpMetadata = (flags[0] & 0x04) != 0,
            IsAnimated = (flags[0] & 0x02) != 0
        };

        _hasAlpha = _extendedInfo.HasAlpha;
        _isAnimated = _extendedInfo.IsAnimated;

        // Read canvas dimensions
        byte[] dimBytes = new byte[6];
        if (_stream.Read(dimBytes, 0, 6) != 6)
            throw new WebPDecodingException("Failed to read VP8X dimensions");

        _width = (dimBytes[0] | (dimBytes[1] << 8) | (dimBytes[2] << 16)) + 1;
        _height = (dimBytes[3] | (dimBytes[4] << 8) | (dimBytes[5] << 16)) + 1;

        _extendedInfo.CanvasWidth = _width;
        _extendedInfo.CanvasHeight = _height;

        // Skip to end of VP8X chunk
        long chunkEnd = _stream.Position - 10 + chunk.SizeRounded;
        _stream.Position = chunkEnd;

        // Read remaining chunks
        long maxPosition = 12 + fileSize;
        _loopCount = 0;

        while (_stream.Position < maxPosition - 8)
        {
            var subChunk = ReadChunkHeader();
            long chunkDataStart = _stream.Position;

            switch (subChunk.Type)
            {
                case WebPChunkType.ANIM:
                    ReadAnimChunk(subChunk);
                    break;

                case WebPChunkType.ANMF:
                    ReadAnmfChunk(subChunk);
                    break;

                case WebPChunkType.VP8:
                case WebPChunkType.VP8L:
                case WebPChunkType.ALPH:
                    // Non-animated image data - skip
                    if (!_isAnimated)
                    {
                        _frames.Add(new WebPFrameInfo
                        {
                            DataStart = chunkDataStart,
                            DataSize = subChunk.Size,
                            IsLossy = subChunk.Type == WebPChunkType.VP8,
                            Width = _width,
                            Height = _height,
                            OffsetX = 0,
                            OffsetY = 0,
                            Duration = 0
                        });
                    }
                    break;
            }

            // Move to next chunk
            _stream.Position = chunkDataStart + subChunk.SizeRounded;
        }
    }

    private void ReadAnimChunk(WebPChunk chunk)
    {
        // Read background color (BGRA)
        _backgroundColor = new byte[4];
        if (_stream.Read(_backgroundColor, 0, 4) != 4)
            throw new WebPDecodingException("Failed to read ANIM background color");

        // Read loop count
        byte[] loopBytes = new byte[2];
        if (_stream.Read(loopBytes, 0, 2) != 2)
            throw new WebPDecodingException("Failed to read ANIM loop count");

        _loopCount = BitConverter.ToUInt16(loopBytes, 0);
    }

    private void ReadAnmfChunk(WebPChunk chunk)
    {
        long frameStart = _stream.Position;

        // Read frame offset (3 bytes each for X and Y)
        byte[] offsetBytes = new byte[6];
        if (_stream.Read(offsetBytes, 0, 6) != 6)
            throw new WebPDecodingException("Failed to read ANMF offset");

        int offsetX = (offsetBytes[0] | (offsetBytes[1] << 8) | (offsetBytes[2] << 16)) * 2;
        int offsetY = (offsetBytes[3] | (offsetBytes[4] << 8) | (offsetBytes[5] << 16)) * 2;

        // Read frame dimensions
        byte[] dimBytes = new byte[6];
        if (_stream.Read(dimBytes, 0, 6) != 6)
            throw new WebPDecodingException("Failed to read ANMF dimensions");

        int frameWidth = (dimBytes[0] | (dimBytes[1] << 8) | (dimBytes[2] << 16)) + 1;
        int frameHeight = (dimBytes[3] | (dimBytes[4] << 8) | (dimBytes[5] << 16)) + 1;

        // Read duration and flags
        byte[] durationBytes = new byte[4];
        if (_stream.Read(durationBytes, 0, 4) != 4)
            throw new WebPDecodingException("Failed to read ANMF duration");

        int duration = durationBytes[0] | (durationBytes[1] << 8) | (durationBytes[2] << 16);
        byte frameFlags = durationBytes[3];
        bool useAlphaBlending = (frameFlags & 0x02) == 0;
        bool disposeToBackground = (frameFlags & 0x01) != 0;

        // Read frame data chunk
        var dataChunk = ReadChunkHeader();
        long dataStart = _stream.Position;

        bool isLossy = dataChunk.Type == WebPChunkType.VP8 || dataChunk.Type == WebPChunkType.ALPH;

        // If ALPH chunk, there should be a VP8 chunk following
        long alphaStart = 0;
        uint alphaSize = 0;
        if (dataChunk.Type == WebPChunkType.ALPH)
        {
            alphaStart = dataStart;
            alphaSize = dataChunk.Size;
            _stream.Position = dataStart + dataChunk.SizeRounded;
            dataChunk = ReadChunkHeader();
            dataStart = _stream.Position;
            isLossy = true;
        }

        _frames.Add(new WebPFrameInfo
        {
            DataStart = dataStart,
            DataSize = dataChunk.Size,
            IsLossy = isLossy,
            Width = frameWidth,
            Height = frameHeight,
            OffsetX = offsetX,
            OffsetY = offsetY,
            Duration = duration,
            UseAlphaBlending = useAlphaBlending,
            DisposeToBackground = disposeToBackground,
            AlphaDataStart = alphaStart,
            AlphaDataSize = alphaSize
        });
    }

    private Image DecodeSingleFrame()
    {
        if (_frames.Count == 0)
            throw new WebPDecodingException("No image data found");

        var frameInfo = _frames[0];
        byte[] rgba = DecodeFrameData(frameInfo);

        var buffer = new PixelBuffer(_width, _height, rgba);
        var frame = new ImageFrame(buffer);

        return new Image(frame, _hasAlpha);
    }

    private Image DecodeAnimated()
    {
        var frames = new List<ImageFrame>();

        // Canvas for compositing
        byte[] canvas = new byte[_width * _height * 4];

        // Initialize with background color if available
        if (_backgroundColor != null)
        {
            // Convert BGRA to RGBA
            byte r = _backgroundColor[2];
            byte g = _backgroundColor[1];
            byte b = _backgroundColor[0];
            byte a = _backgroundColor[3];

            for (int i = 0; i < canvas.Length; i += 4)
            {
                canvas[i] = r;
                canvas[i + 1] = g;
                canvas[i + 2] = b;
                canvas[i + 3] = a;
            }
        }

        WebPFrameInfo previousFrame = null;

        foreach (var frameInfo in _frames)
        {
            // Dispose previous frame if needed
            if (previousFrame != null && previousFrame.DisposeToBackground)
            {
                ClearRegion(canvas, previousFrame.OffsetX, previousFrame.OffsetY,
                    previousFrame.Width, previousFrame.Height);
            }

            // Decode frame
            byte[] frameData = DecodeFrameData(frameInfo);

            // Composite onto canvas
            CompositeFrame(canvas, frameData, frameInfo);

            // Create image frame from current canvas state
            byte[] frameBuffer = new byte[canvas.Length];
            Array.Copy(canvas, frameBuffer, canvas.Length);

            var pixelBuffer = new PixelBuffer(_width, _height, frameBuffer);
            var imageFrame = new ImageFrame(pixelBuffer)
            {
                Duration = TimeSpan.FromMilliseconds(frameInfo.Duration)
            };

            frames.Add(imageFrame);
            previousFrame = frameInfo;
        }

        return new Image(frames, _hasAlpha, _loopCount);
    }

    private byte[] DecodeFrameData(WebPFrameInfo frameInfo)
    {
        _stream.Position = frameInfo.DataStart;
        byte[] data = new byte[frameInfo.DataSize];
        if (_stream.Read(data, 0, (int)frameInfo.DataSize) != (int)frameInfo.DataSize)
            throw new WebPDecodingException("Failed to read frame data");

        byte[] rgba = new byte[frameInfo.Width * frameInfo.Height * 4];

        if (frameInfo.IsLossy)
        {
            using var ms = new MemoryStream(data);
            var vp8Decoder = new VP8Decoder(ms);
            var vp8Frame = vp8Decoder.Decode();
            vp8Frame.FillRgba(rgba, true);

            // Handle alpha channel if present
            if (frameInfo.AlphaDataSize > 0)
            {
                _stream.Position = frameInfo.AlphaDataStart;
                byte[] alphaData = new byte[frameInfo.AlphaDataSize];
                if (_stream.Read(alphaData, 0, (int)frameInfo.AlphaDataSize) != (int)frameInfo.AlphaDataSize)
                    throw new WebPDecodingException("Failed to read alpha data");

                ApplyAlphaChannel(rgba, alphaData, frameInfo.Width, frameInfo.Height);
            }
        }
        else
        {
            var vp8LDecoder = new VP8LDecoder(data);
            vp8LDecoder.DecodeFrame((uint)frameInfo.Width, (uint)frameInfo.Height, false, rgba);
        }

        return rgba;
    }

    private void ApplyAlphaChannel(byte[] rgba, byte[] alphaData, int width, int height)
    {
        // Parse alpha chunk header
        if (alphaData.Length < 1)
            return;

        byte header = alphaData[0];
        bool preprocessing = (header & 0x30) != 0;
        int filteringMethod = (header >> 2) & 0x03;
        int compressionMethod = header & 0x03;

        byte[] alpha;

        if (compressionMethod == 0)
        {
            // Uncompressed
            alpha = new byte[width * height];
            int srcOffset = 1;
            int remaining = Math.Min(alpha.Length, alphaData.Length - 1);
            Array.Copy(alphaData, srcOffset, alpha, 0, remaining);
        }
        else if (compressionMethod == 1)
        {
            // Lossless compression
            byte[] losslessData = new byte[alphaData.Length - 1];
            Array.Copy(alphaData, 1, losslessData, 0, losslessData.Length);

            var decoder = new VP8LDecoder(losslessData);
            byte[] decoded = new byte[width * height * 4];
            decoder.DecodeFrame((uint)width, (uint)height, true, decoded);

            alpha = new byte[width * height];
            for (int i = 0; i < alpha.Length; i++)
                alpha[i] = decoded[i * 4 + 1]; // Green channel contains alpha
        }
        else
        {
            throw new WebPDecodingException($"Invalid alpha compression method: {compressionMethod}");
        }

        // Apply filtering
        if (filteringMethod != 0)
        {
            ApplyAlphaFilter(alpha, width, height, filteringMethod);
        }

        // Copy alpha to RGBA buffer
        for (int i = 0; i < width * height; i++)
        {
            rgba[i * 4 + 3] = alpha[i];
        }
    }

    private void ApplyAlphaFilter(byte[] alpha, int width, int height, int method)
    {
        // Apply delta filtering
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = y * width + x;
                byte predictor = GetAlphaPredictor(x, y, width, method, alpha);
                alpha[idx] = (byte)(alpha[idx] + predictor);
            }
        }
    }

    private static byte GetAlphaPredictor(int x, int y, int width, int method, byte[] alpha)
    {
        byte left = x > 0 ? alpha[y * width + x - 1] : (byte)0;
        byte top = y > 0 ? alpha[(y - 1) * width + x] : (byte)0;

        return method switch
        {
            1 => left,  // Horizontal
            2 => top,   // Vertical
            3 => (byte)((left + top) / 2), // Gradient
            _ => 0
        };
    }

    private void CompositeFrame(byte[] canvas, byte[] frame, WebPFrameInfo info)
    {
        int canvasWidth = _width;

        for (int y = 0; y < info.Height; y++)
        {
            int srcRow = y * info.Width * 4;
            int dstRow = ((info.OffsetY + y) * canvasWidth + info.OffsetX) * 4;

            for (int x = 0; x < info.Width; x++)
            {
                int srcIdx = srcRow + x * 4;
                int dstIdx = dstRow + x * 4;

                byte srcR = frame[srcIdx];
                byte srcG = frame[srcIdx + 1];
                byte srcB = frame[srcIdx + 2];
                byte srcA = frame[srcIdx + 3];

                if (info.UseAlphaBlending && srcA < 255)
                {
                    // Alpha blending
                    byte dstR = canvas[dstIdx];
                    byte dstG = canvas[dstIdx + 1];
                    byte dstB = canvas[dstIdx + 2];
                    byte dstA = canvas[dstIdx + 3];

                    int outA = srcA + (dstA * (255 - srcA) / 255);
                    if (outA > 0)
                    {
                        canvas[dstIdx] = (byte)((srcR * srcA + dstR * dstA * (255 - srcA) / 255) / outA);
                        canvas[dstIdx + 1] = (byte)((srcG * srcA + dstG * dstA * (255 - srcA) / 255) / outA);
                        canvas[dstIdx + 2] = (byte)((srcB * srcA + dstB * dstA * (255 - srcA) / 255) / outA);
                        canvas[dstIdx + 3] = (byte)outA;
                    }
                }
                else
                {
                    // Direct replacement
                    canvas[dstIdx] = srcR;
                    canvas[dstIdx + 1] = srcG;
                    canvas[dstIdx + 2] = srcB;
                    canvas[dstIdx + 3] = srcA;
                }
            }
        }
    }

    private void ClearRegion(byte[] canvas, int x, int y, int width, int height)
    {
        byte r = _backgroundColor?[2] ?? 0;
        byte g = _backgroundColor?[1] ?? 0;
        byte b = _backgroundColor?[0] ?? 0;
        byte a = _backgroundColor?[3] ?? 0;

        int canvasWidth = _width;

        for (int dy = 0; dy < height; dy++)
        {
            int dstRow = ((y + dy) * canvasWidth + x) * 4;
            for (int dx = 0; dx < width; dx++)
            {
                int dstIdx = dstRow + dx * 4;
                canvas[dstIdx] = r;
                canvas[dstIdx + 1] = g;
                canvas[dstIdx + 2] = b;
                canvas[dstIdx + 3] = a;
            }
        }
    }
}

/// <summary>
/// Information about a single WebP frame.
/// </summary>
internal class WebPFrameInfo
{
    public long DataStart { get; set; }
    public uint DataSize { get; set; }
    public bool IsLossy { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int OffsetX { get; set; }
    public int OffsetY { get; set; }
    public int Duration { get; set; }
    public bool UseAlphaBlending { get; set; } = true;
    public bool DisposeToBackground { get; set; }
    public long AlphaDataStart { get; set; }
    public uint AlphaDataSize { get; set; }
}
