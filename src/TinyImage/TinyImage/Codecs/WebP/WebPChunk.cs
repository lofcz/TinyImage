using System;

namespace TinyImage.Codecs.WebP;

/// <summary>
/// WebP RIFF chunk types.
/// </summary>
internal enum WebPChunkType
{
    /// <summary>RIFF container header</summary>
    RIFF,
    /// <summary>WebP file type</summary>
    WEBP,
    /// <summary>VP8 lossy bitstream</summary>
    VP8,
    /// <summary>VP8L lossless bitstream</summary>
    VP8L,
    /// <summary>Extended file format</summary>
    VP8X,
    /// <summary>Animation parameters</summary>
    ANIM,
    /// <summary>Animation frame</summary>
    ANMF,
    /// <summary>Alpha channel</summary>
    ALPH,
    /// <summary>ICC color profile</summary>
    ICCP,
    /// <summary>EXIF metadata</summary>
    EXIF,
    /// <summary>XMP metadata</summary>
    XMP,
    /// <summary>Unknown chunk type</summary>
    Unknown
}

/// <summary>
/// Represents a WebP RIFF chunk header.
/// </summary>
internal readonly struct WebPChunk
{
    /// <summary>Chunk type</summary>
    public readonly WebPChunkType Type;

    /// <summary>Chunk size in bytes (excluding padding)</summary>
    public readonly uint Size;

    /// <summary>Chunk size including padding (aligned to 2 bytes)</summary>
    public readonly uint SizeRounded;

    /// <summary>Raw FourCC bytes for unknown chunks</summary>
    public readonly byte[] FourCC;

    public WebPChunk(WebPChunkType type, uint size, byte[] fourCC = null)
    {
        Type = type;
        Size = size;
        // WebP chunks are padded to even byte boundaries
        SizeRounded = size + (size & 1);
        FourCC = fourCC ?? Array.Empty<byte>();
    }

    /// <summary>
    /// Parses a FourCC code into a chunk type.
    /// </summary>
    public static WebPChunkType ParseFourCC(byte[] fourCC)
    {
        if (fourCC == null || fourCC.Length != 4)
            return WebPChunkType.Unknown;

        // Check known chunk types
        if (fourCC[0] == 'R' && fourCC[1] == 'I' && fourCC[2] == 'F' && fourCC[3] == 'F')
            return WebPChunkType.RIFF;
        if (fourCC[0] == 'W' && fourCC[1] == 'E' && fourCC[2] == 'B' && fourCC[3] == 'P')
            return WebPChunkType.WEBP;
        if (fourCC[0] == 'V' && fourCC[1] == 'P' && fourCC[2] == '8' && fourCC[3] == ' ')
            return WebPChunkType.VP8;
        if (fourCC[0] == 'V' && fourCC[1] == 'P' && fourCC[2] == '8' && fourCC[3] == 'L')
            return WebPChunkType.VP8L;
        if (fourCC[0] == 'V' && fourCC[1] == 'P' && fourCC[2] == '8' && fourCC[3] == 'X')
            return WebPChunkType.VP8X;
        if (fourCC[0] == 'A' && fourCC[1] == 'N' && fourCC[2] == 'I' && fourCC[3] == 'M')
            return WebPChunkType.ANIM;
        if (fourCC[0] == 'A' && fourCC[1] == 'N' && fourCC[2] == 'M' && fourCC[3] == 'F')
            return WebPChunkType.ANMF;
        if (fourCC[0] == 'A' && fourCC[1] == 'L' && fourCC[2] == 'P' && fourCC[3] == 'H')
            return WebPChunkType.ALPH;
        if (fourCC[0] == 'I' && fourCC[1] == 'C' && fourCC[2] == 'C' && fourCC[3] == 'P')
            return WebPChunkType.ICCP;
        if (fourCC[0] == 'E' && fourCC[1] == 'X' && fourCC[2] == 'I' && fourCC[3] == 'F')
            return WebPChunkType.EXIF;
        if (fourCC[0] == 'X' && fourCC[1] == 'M' && fourCC[2] == 'P' && fourCC[3] == ' ')
            return WebPChunkType.XMP;

        return WebPChunkType.Unknown;
    }

    /// <summary>
    /// Converts a chunk type to its FourCC bytes.
    /// </summary>
    public static byte[] ToFourCC(WebPChunkType type)
    {
        return type switch
        {
            WebPChunkType.RIFF => new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' },
            WebPChunkType.WEBP => new byte[] { (byte)'W', (byte)'E', (byte)'B', (byte)'P' },
            WebPChunkType.VP8 => new byte[] { (byte)'V', (byte)'P', (byte)'8', (byte)' ' },
            WebPChunkType.VP8L => new byte[] { (byte)'V', (byte)'P', (byte)'8', (byte)'L' },
            WebPChunkType.VP8X => new byte[] { (byte)'V', (byte)'P', (byte)'8', (byte)'X' },
            WebPChunkType.ANIM => new byte[] { (byte)'A', (byte)'N', (byte)'I', (byte)'M' },
            WebPChunkType.ANMF => new byte[] { (byte)'A', (byte)'N', (byte)'M', (byte)'F' },
            WebPChunkType.ALPH => new byte[] { (byte)'A', (byte)'L', (byte)'P', (byte)'H' },
            WebPChunkType.ICCP => new byte[] { (byte)'I', (byte)'C', (byte)'C', (byte)'P' },
            WebPChunkType.EXIF => new byte[] { (byte)'E', (byte)'X', (byte)'I', (byte)'F' },
            WebPChunkType.XMP => new byte[] { (byte)'X', (byte)'M', (byte)'P', (byte)' ' },
            _ => new byte[] { 0, 0, 0, 0 }
        };
    }
}
