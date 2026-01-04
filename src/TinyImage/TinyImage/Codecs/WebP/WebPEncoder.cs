using System;
using System.IO;
using TinyImage.Codecs.WebP.Lossless;
using TinyImage.Codecs.WebP.Lossy;

namespace TinyImage.Codecs.WebP;

/// <summary>
/// WebP encoder that supports both lossy and lossless encoding.
/// Handles RIFF container format and animation support.
/// </summary>
internal class WebPEncoder
{
    /// <summary>
    /// Encoding options for WebP.
    /// </summary>
    public class EncoderOptions
    {
        /// <summary>
        /// Whether to use lossless encoding. Default is false (lossy).
        /// </summary>
        public bool Lossless { get; set; }

        /// <summary>
        /// Quality for lossy encoding (0-100). Default is 75.
        /// Ignored for lossless encoding.
        /// </summary>
        public int Quality { get; set; } = 75;

        /// <summary>
        /// Loop count for animated images. 0 means infinite loop.
        /// </summary>
        public int LoopCount { get; set; }
    }

    private readonly Stream _stream;
    private readonly EncoderOptions _options;

    public WebPEncoder(Stream stream, EncoderOptions options = null)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _options = options ?? new EncoderOptions();
    }

    /// <summary>
    /// Encodes an image to WebP format.
    /// </summary>
    public void Encode(Image image)
    {
        if (image == null)
            throw new ArgumentNullException(nameof(image));

        if (image.Frames.Count == 0)
            throw new WebPEncodingException("Image has no frames to encode");

        if (image.Frames.Count > 1)
        {
            EncodeAnimated(image);
        }
        else
        {
            EncodeSingle(image.Frames[0], image.Width, image.Height, image.HasAlpha);
        }
    }

    private void EncodeSingle(ImageFrame frame, int width, int height, bool hasAlpha)
    {
        byte[] rgba = frame.GetPixelData();

        using var payloadStream = new MemoryStream();

        if (_options.Lossless)
        {
            var encoder = new VP8LEncoder(payloadStream);
            encoder.Encode(rgba, width, height, hasAlpha);
        }
        else
        {
            var encoder = new VP8Encoder(payloadStream);
            encoder.Encode(rgba, width, height, _options.Quality);
        }

        byte[] payload = payloadStream.ToArray();
        WriteRiffContainer(payload, width, height, _options.Lossless, hasAlpha);
    }

    private void EncodeAnimated(Image image)
    {
        using var animDataStream = new MemoryStream();

        // Write ANIM chunk
        WriteAnimChunk(animDataStream, image.LoopCount);

        // Write each frame as ANMF chunk
        for (int i = 0; i < image.Frames.Count; i++)
        {
            var frame = image.Frames[i];
            WriteAnimFrame(animDataStream, frame, i == 0);
        }

        byte[] animData = animDataStream.ToArray();
        WriteAnimatedRiffContainer(animData, image.Width, image.Height, image.HasAlpha);
    }

    private void WriteRiffContainer(byte[] payload, int width, int height, bool lossless, bool hasAlpha)
    {
        string chunkType = lossless ? "VP8L" : "VP8 ";
        int fileSize = 4 + 8 + payload.Length; // "WEBP" + chunk header + payload

        // Add padding if needed
        bool needsPadding = (payload.Length % 2) != 0;
        if (needsPadding)
            fileSize++;

        WriteAscii("RIFF");
        WriteUInt32((uint)fileSize);
        WriteAscii("WEBP");

        WriteAscii(chunkType);
        WriteUInt32((uint)payload.Length);
        _stream.Write(payload, 0, payload.Length);

        if (needsPadding)
            _stream.WriteByte(0);
    }

    private void WriteAnimatedRiffContainer(byte[] animData, int width, int height, bool hasAlpha)
    {
        // Calculate VP8X extended header size
        int vp8xSize = 10; // VP8X chunk data
        int fileSize = 4 + 8 + vp8xSize + animData.Length; // "WEBP" + VP8X header + VP8X data + anim data

        // Ensure padding for VP8X if needed
        bool vp8xNeedsPadding = (vp8xSize % 2) != 0;
        if (vp8xNeedsPadding)
            fileSize++;

        WriteAscii("RIFF");
        WriteUInt32((uint)fileSize);
        WriteAscii("WEBP");

        // Write VP8X chunk
        WriteAscii("VP8X");
        WriteUInt32((uint)vp8xSize);

        // VP8X flags: animation=1, alpha if applicable
        byte flags = 0x02; // Animation flag
        if (hasAlpha)
            flags |= 0x10; // Alpha flag
        _stream.WriteByte(flags);
        WriteBytes(new byte[3]); // Reserved

        // Canvas width - 1 (24-bit)
        WriteUInt24((uint)(width - 1));
        // Canvas height - 1 (24-bit)
        WriteUInt24((uint)(height - 1));

        if (vp8xNeedsPadding)
            _stream.WriteByte(0);

        // Write animation data (ANIM and ANMF chunks)
        _stream.Write(animData, 0, animData.Length);
    }

    private void WriteAnimChunk(Stream stream, int loopCount)
    {
        var bw = new BinaryWriter(stream);
        
        // "ANIM" chunk
        bw.Write(new[] { (byte)'A', (byte)'N', (byte)'I', (byte)'M' });
        bw.Write((uint)6); // Chunk size

        // Background color (BGRA)
        bw.Write((uint)0); // Transparent black

        // Loop count
        bw.Write((ushort)loopCount);
    }

    private void WriteAnimFrame(Stream stream, ImageFrame frame, bool isFirst)
    {
        byte[] rgba = frame.GetPixelData();
        int width = frame.Width;
        int height = frame.Height;
        bool hasAlpha = true; // Assume animated frames have alpha

        // Encode the frame
        using var frameDataStream = new MemoryStream();
        
        if (_options.Lossless)
        {
            var encoder = new VP8LEncoder(frameDataStream);
            encoder.Encode(rgba, width, height, hasAlpha);
        }
        else
        {
            var encoder = new VP8Encoder(frameDataStream);
            encoder.Encode(rgba, width, height, _options.Quality);
        }

        byte[] frameData = frameDataStream.ToArray();

        // Write ANMF chunk
        string subChunkType = _options.Lossless ? "VP8L" : "VP8 ";
        int anmfDataSize = 16 + 8 + frameData.Length; // ANMF data + sub-chunk header + sub-chunk data

        bool frameNeedsPadding = (frameData.Length % 2) != 0;
        bool anmfNeedsPadding = (anmfDataSize % 2) != 0;

        var bw = new BinaryWriter(stream);
        bw.Write(new[] { (byte)'A', (byte)'N', (byte)'M', (byte)'F' });
        bw.Write((uint)anmfDataSize);

        // Frame X offset (24-bit)
        WriteUInt24To(stream, 0);
        // Frame Y offset (24-bit)
        WriteUInt24To(stream, 0);

        // Frame width - 1 (24-bit)
        WriteUInt24To(stream, (uint)(width - 1));
        // Frame height - 1 (24-bit)
        WriteUInt24To(stream, (uint)(height - 1));

        // Frame duration (24-bit)
        WriteUInt24To(stream, (uint)frame.Duration.TotalMilliseconds);

        // Frame flags
        byte frameFlags = 0;
        if (hasAlpha)
            frameFlags |= 0x02; // Alpha blending
        // Use dispose method = do not dispose
        stream.WriteByte(frameFlags);

        // Write the frame bitstream
        bw.Write(new[] { (byte)subChunkType[0], (byte)subChunkType[1], (byte)subChunkType[2], (byte)subChunkType[3] });
        bw.Write((uint)frameData.Length);
        stream.Write(frameData, 0, frameData.Length);

        if (frameNeedsPadding)
            stream.WriteByte(0);
    }

    private void WriteAscii(string text)
    {
        foreach (char c in text)
            _stream.WriteByte((byte)c);
    }

    private void WriteBytes(byte[] bytes)
    {
        _stream.Write(bytes, 0, bytes.Length);
    }

    private void WriteUInt32(uint value)
    {
        _stream.WriteByte((byte)(value & 0xFF));
        _stream.WriteByte((byte)((value >> 8) & 0xFF));
        _stream.WriteByte((byte)((value >> 16) & 0xFF));
        _stream.WriteByte((byte)((value >> 24) & 0xFF));
    }

    private void WriteUInt24(uint value)
    {
        _stream.WriteByte((byte)(value & 0xFF));
        _stream.WriteByte((byte)((value >> 8) & 0xFF));
        _stream.WriteByte((byte)((value >> 16) & 0xFF));
    }

    private static void WriteUInt24To(Stream stream, uint value)
    {
        stream.WriteByte((byte)(value & 0xFF));
        stream.WriteByte((byte)((value >> 8) & 0xFF));
        stream.WriteByte((byte)((value >> 16) & 0xFF));
    }
}
