using System.Runtime.CompilerServices;

namespace TinyImage.Codecs.Qoi;

/// <summary>
/// Constants for QOI (Quite OK Image) format encoding and decoding.
/// Based on the QOI specification by Dominic Szablewski.
/// </summary>
internal static class QoiConstants
{
    #region Op-codes

    /// <summary>
    /// 2-bit tag 00: Index into the previously seen pixels array.
    /// </summary>
    public const byte Index = 0x00;

    /// <summary>
    /// 2-bit tag 01: Small difference from previous pixel (-2..1 per channel).
    /// </summary>
    public const byte Diff = 0x40;

    /// <summary>
    /// 2-bit tag 10: Luma-based difference encoding.
    /// </summary>
    public const byte Luma = 0x80;

    /// <summary>
    /// 2-bit tag 11: Run-length encoding of the previous pixel.
    /// </summary>
    public const byte Run = 0xC0;

    /// <summary>
    /// 8-bit tag: Full RGB values follow.
    /// </summary>
    public const byte Rgb = 0xFE;

    /// <summary>
    /// 8-bit tag: Full RGBA values follow.
    /// </summary>
    public const byte Rgba = 0xFF;

    /// <summary>
    /// Mask for extracting 2-bit tags.
    /// </summary>
    public const byte Mask2 = 0xC0;

    #endregion

    #region Format Constants

    /// <summary>
    /// Size of the QOI file header in bytes.
    /// </summary>
    public const int HeaderSize = 14;

    /// <summary>
    /// Size of the hash table for previously seen pixels.
    /// </summary>
    public const int HashTableSize = 64;

    /// <summary>
    /// Maximum number of pixels supported (400 million).
    /// This is a safety limit to prevent excessive memory allocation.
    /// </summary>
    public const long MaxPixels = 400_000_000;

    /// <summary>
    /// Maximum run length that can be encoded (62).
    /// Values 63 and 64 are reserved for RGB and RGBA tags.
    /// </summary>
    public const int MaxRunLength = 62;

    /// <summary>
    /// QOI file magic bytes: "qoif".
    /// </summary>
    public static readonly byte[] Magic = { (byte)'q', (byte)'o', (byte)'i', (byte)'f' };

    /// <summary>
    /// End marker: 7 bytes of 0x00 followed by 0x01.
    /// </summary>
    public static readonly byte[] Padding = { 0, 0, 0, 0, 0, 0, 0, 1 };

    #endregion

    #region Hash Function

    /// <summary>
    /// Calculates the hash table index for a pixel color.
    /// Formula: (r * 3 + g * 5 + b * 7 + a * 11) % 64
    /// </summary>
    /// <param name="r">Red channel value.</param>
    /// <param name="g">Green channel value.</param>
    /// <param name="b">Blue channel value.</param>
    /// <param name="a">Alpha channel value.</param>
    /// <returns>Hash table index (0-63).</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CalculateHashIndex(byte r, byte g, byte b, byte a)
    {
        return (r * 3 + g * 5 + b * 7 + a * 11) & 63;
    }

    /// <summary>
    /// Calculates the hash table index for a packed RGBA pixel.
    /// </summary>
    /// <param name="packedPixel">Pixel packed as RGBA in big-endian order.</param>
    /// <returns>Hash table index (0-63).</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CalculateHashIndex(int packedPixel)
    {
        return (((packedPixel >> 24) & 0xFF) * 3 +
                ((packedPixel >> 16) & 0xFF) * 5 +
                ((packedPixel >> 8) & 0xFF) * 7 +
                (packedPixel & 0xFF) * 11) & 63;
    }

    #endregion

    #region Magic Validation

    /// <summary>
    /// Checks if the given bytes match the QOI magic signature.
    /// </summary>
    /// <param name="data">The bytes to check (must be at least 4 bytes).</param>
    /// <returns>True if the magic matches "qoif".</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsValidMagic(byte[] data)
    {
        return data.Length >= 4 &&
               data[0] == Magic[0] &&
               data[1] == Magic[1] &&
               data[2] == Magic[2] &&
               data[3] == Magic[3];
    }

    #endregion
}
