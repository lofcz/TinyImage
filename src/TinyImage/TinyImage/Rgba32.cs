using System;
using System.Runtime.InteropServices;

namespace TinyImage;

/// <summary>
/// Represents a 32-bit RGBA color (4 bytes per pixel).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct Rgba32 : IEquatable<Rgba32>
{
    /// <summary>
    /// The red component.
    /// </summary>
    public readonly byte R;

    /// <summary>
    /// The green component.
    /// </summary>
    public readonly byte G;

    /// <summary>
    /// The blue component.
    /// </summary>
    public readonly byte B;

    /// <summary>
    /// The alpha component.
    /// </summary>
    public readonly byte A;

    /// <summary>
    /// Creates a new <see cref="Rgba32"/> color.
    /// </summary>
    /// <param name="r">The red component (0-255).</param>
    /// <param name="g">The green component (0-255).</param>
    /// <param name="b">The blue component (0-255).</param>
    /// <param name="a">The alpha component (0-255). Default is 255 (fully opaque).</param>
    public Rgba32(byte r, byte g, byte b, byte a = 255)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    /// <summary>
    /// Creates a new <see cref="Rgba32"/> color from a 32-bit packed value (0xRRGGBBAA).
    /// </summary>
    /// <param name="packedValue">The packed RGBA value.</param>
    public Rgba32(uint packedValue)
    {
        R = (byte)(packedValue >> 24);
        G = (byte)(packedValue >> 16);
        B = (byte)(packedValue >> 8);
        A = (byte)packedValue;
    }

    /// <summary>
    /// Gets the packed 32-bit value (0xRRGGBBAA).
    /// </summary>
    public uint PackedValue => (uint)(R << 24 | G << 16 | B << 8 | A);

    /// <summary>
    /// Transparent black (0, 0, 0, 0).
    /// </summary>
    public static Rgba32 Transparent => new(0, 0, 0, 0);

    /// <summary>
    /// Opaque black (0, 0, 0, 255).
    /// </summary>
    public static Rgba32 Black => new(0, 0, 0, 255);

    /// <summary>
    /// Opaque white (255, 255, 255, 255).
    /// </summary>
    public static Rgba32 White => new(255, 255, 255, 255);

    /// <inheritdoc />
    public bool Equals(Rgba32 other) => R == other.R && G == other.G && B == other.B && A == other.A;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Rgba32 other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => (int)PackedValue;

    /// <inheritdoc />
    public override string ToString() => $"Rgba32({R}, {G}, {B}, {A})";

    /// <summary>
    /// Equality operator.
    /// </summary>
    public static bool operator ==(Rgba32 left, Rgba32 right) => left.Equals(right);

    /// <summary>
    /// Inequality operator.
    /// </summary>
    public static bool operator !=(Rgba32 left, Rgba32 right) => !left.Equals(right);
}
