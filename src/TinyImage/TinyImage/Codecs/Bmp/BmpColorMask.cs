using System.Runtime.CompilerServices;

namespace TinyImage.Codecs.Bmp;

/// <summary>
/// Handles BitFields color mask extraction and scaling.
/// Based on the algorithm from bmp-ts for extracting and scaling
/// color components from masked pixel values.
/// </summary>
internal readonly struct BmpColorMask
{
    private readonly uint _mask;
    private readonly uint _rightShift;
    private readonly uint _scale;
    private readonly bool _hasMask;

    /// <summary>
    /// Creates a new color mask extractor.
    /// </summary>
    /// <param name="mask">The bit mask for this color component.</param>
    public BmpColorMask(uint mask)
    {
        _mask = mask;
        _hasMask = mask != 0;

        if (_hasMask)
        {
            // Find the position of the lowest set bit
            // This is equivalent to: (~mask + 1) & mask
            uint lowestBit = (uint)(-(int)mask) & mask;
            _rightShift = CountTrailingZeros(lowestBit);

            // Calculate how many bits are in the mask
            uint shiftedMask = mask >> (int)_rightShift;
            uint bitsInMask = CountBits(shiftedMask);

            // Scale factor to expand to 8 bits
            // If mask is 5 bits (0x1F), we need to scale by 256/32 = 8
            _scale = bitsInMask < 8 ? 256u >> (int)bitsInMask : 1;
        }
        else
        {
            _rightShift = 0;
            _scale = 0;
        }
    }

    /// <summary>
    /// Extracts and scales the color component from a pixel value.
    /// </summary>
    /// <param name="pixel">The raw pixel value.</param>
    /// <returns>The 8-bit color component value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte Extract(uint pixel)
    {
        if (!_hasMask)
            return 255;

        uint masked = pixel & _mask;
        uint shifted = masked >> (int)_rightShift;
        uint scaled = shifted * _scale;

        // Clamp to byte range
        return (byte)(scaled > 255 ? 255 : scaled);
    }

    /// <summary>
    /// Counts trailing zeros in a value (position of lowest set bit).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint CountTrailingZeros(uint value)
    {
        if (value == 0) return 32;

        uint count = 0;
        while ((value & 1) == 0)
        {
            value >>= 1;
            count++;
        }
        return count;
    }

    /// <summary>
    /// Counts the number of set bits in a value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint CountBits(uint value)
    {
        uint count = 0;
        while (value != 0)
        {
            count += value & 1;
            value >>= 1;
        }
        return count;
    }
}

/// <summary>
/// Contains color masks for all RGBA components.
/// </summary>
internal readonly struct BmpColorMasks
{
    public readonly BmpColorMask Red;
    public readonly BmpColorMask Green;
    public readonly BmpColorMask Blue;
    public readonly BmpColorMask Alpha;

    /// <summary>
    /// Creates color masks from the header's mask values.
    /// </summary>
    public BmpColorMasks(uint redMask, uint greenMask, uint blueMask, uint alphaMask)
    {
        Red = new BmpColorMask(redMask);
        Green = new BmpColorMask(greenMask);
        Blue = new BmpColorMask(blueMask);
        Alpha = new BmpColorMask(alphaMask);
    }

    /// <summary>
    /// Creates color masks from a BMP info header.
    /// </summary>
    public BmpColorMasks(in BmpInfoHeader header)
        : this(header.RedMask, header.GreenMask, header.BlueMask, header.AlphaMask)
    {
    }

    /// <summary>
    /// Extracts RGBA values from a pixel.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ExtractRgba(uint pixel, out byte r, out byte g, out byte b, out byte a)
    {
        r = Red.Extract(pixel);
        g = Green.Extract(pixel);
        b = Blue.Extract(pixel);
        a = Alpha.Extract(pixel);
    }
}
