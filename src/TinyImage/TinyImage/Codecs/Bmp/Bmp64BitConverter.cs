using System;
using System.Runtime.CompilerServices;

namespace TinyImage.Codecs.Bmp;

/// <summary>
/// Converts 64-bit BMP pixel data from s2.13 fixed-point linear light to 8-bit sRGB.
/// 64-bit BMPs store 16-bit per channel RGBA in s2.13 fixed-point format in linear light.
/// Range is -4.0 to +3.999... but typical valid values are 0.0 to 1.0.
/// </summary>
internal static class Bmp64BitConverter
{
    /// <summary>
    /// Conversion mode for 64-bit BMP data.
    /// </summary>
    public enum ConversionMode
    {
        /// <summary>
        /// Convert from linear light s2.13 to sRGB gamma (default).
        /// </summary>
        ToSrgb,

        /// <summary>
        /// Keep linear light values, just scale to 8-bit.
        /// </summary>
        Linear,

        /// <summary>
        /// Keep raw s2.13 values (output as 16-bit).
        /// </summary>
        None
    }

    /// <summary>
    /// Converts an s2.13 fixed-point value to a normalized double (range -4.0 to ~+4.0).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double S2_13ToDouble(ushort s2_13)
    {
        // s2.13 is a signed fixed-point format with 2 integer bits and 13 fractional bits
        // Range: -4.0 to +3.999...
        short signed;

        if (s2_13 >= 0x8000)
            signed = (short)(s2_13 - 0x8000 - 0x7FFF - 1);
        else
            signed = (short)s2_13;

        return signed / 8192.0;
    }

    /// <summary>
    /// Converts a normalized double to an s2.13 fixed-point value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort DoubleToS2_13(double d)
    {
        d = Clamp(d, -4.0, 3.99987793);
        return (ushort)((int)Math.Round(d * 8192.0) & 0xFFFF);
    }
    
    /// <summary>
    /// Clamps a value between min and max (for netstandard2.0 compatibility).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Clamp(double value, double min, double max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    /// <summary>
    /// Applies sRGB gamma curve to a linear light value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double LinearToSrgb(double linear)
    {
        if (linear <= 0.0031308)
            return 12.92 * linear;
        else
            return 1.055 * Math.Pow(linear, 1.0 / 2.4) - 0.055;
    }

    /// <summary>
    /// Applies inverse sRGB gamma curve (sRGB to linear).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double SrgbToLinear(double srgb)
    {
        if (srgb <= 0.04045)
            return srgb / 12.92;
        else
            return Math.Pow((srgb + 0.055) / 1.055, 2.4);
    }

    /// <summary>
    /// Converts a 64-bit pixel (4x ushort in BGR_A order) to RGBA32.
    /// </summary>
    /// <param name="b">Blue channel (s2.13)</param>
    /// <param name="g">Green channel (s2.13)</param>
    /// <param name="r">Red channel (s2.13)</param>
    /// <param name="a">Alpha channel (s2.13)</param>
    /// <param name="mode">Conversion mode</param>
    /// <returns>Converted RGBA32 color</returns>
    public static Rgba32 ConvertPixel(ushort b, ushort g, ushort r, ushort a, ConversionMode mode = ConversionMode.ToSrgb)
    {
        double rd = S2_13ToDouble(r);
        double gd = S2_13ToDouble(g);
        double bd = S2_13ToDouble(b);
        double ad = S2_13ToDouble(a);

        // Clamp to valid [0,1] range for display
        rd = Clamp(rd, 0.0, 1.0);
        gd = Clamp(gd, 0.0, 1.0);
        bd = Clamp(bd, 0.0, 1.0);
        ad = Clamp(ad, 0.0, 1.0);

        if (mode == ConversionMode.ToSrgb)
        {
            // Apply sRGB gamma to RGB channels (not alpha)
            rd = LinearToSrgb(rd);
            gd = LinearToSrgb(gd);
            bd = LinearToSrgb(bd);
        }

        // Convert to 8-bit
        byte rb = (byte)(rd * 255.0 + 0.5);
        byte gb = (byte)(gd * 255.0 + 0.5);
        byte bb = (byte)(bd * 255.0 + 0.5);
        byte ab = (byte)(ad * 255.0 + 0.5);

        return new Rgba32(rb, gb, bb, ab);
    }

    /// <summary>
    /// Converts an RGBA32 color to 64-bit format (4x ushort s2.13).
    /// </summary>
    /// <param name="color">Input color</param>
    /// <param name="mode">Conversion mode</param>
    /// <param name="r">Output red (s2.13)</param>
    /// <param name="g">Output green (s2.13)</param>
    /// <param name="b">Output blue (s2.13)</param>
    /// <param name="a">Output alpha (s2.13)</param>
    public static void ConvertToS2_13(Rgba32 color, ConversionMode mode, out ushort r, out ushort g, out ushort b, out ushort a)
    {
        double rd = color.R / 255.0;
        double gd = color.G / 255.0;
        double bd = color.B / 255.0;
        double ad = color.A / 255.0;

        if (mode == ConversionMode.ToSrgb)
        {
            // Convert from sRGB to linear light for storage
            rd = SrgbToLinear(rd);
            gd = SrgbToLinear(gd);
            bd = SrgbToLinear(bd);
            // Alpha is not gamma-corrected
        }

        r = DoubleToS2_13(rd);
        g = DoubleToS2_13(gd);
        b = DoubleToS2_13(bd);
        a = DoubleToS2_13(ad);
    }
}
