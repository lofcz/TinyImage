using System.Runtime.CompilerServices;

namespace TinyImage;

internal static class MathExt
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Clamp(int value, int min, int max)
    {
        if (value < min) return min;
        return value > max ? max : value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte ClampToByte(double value)
    {
        return value switch
        {
            < 0 => 0,
            > 255 => 255,
            _ => (byte)(value + 0.5)
        };
    }
}