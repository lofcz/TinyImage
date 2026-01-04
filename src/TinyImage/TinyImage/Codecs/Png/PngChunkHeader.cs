using System;

namespace TinyImage.Codecs.Png;

/// <summary>
/// The header for a data chunk in a PNG file.
/// </summary>
internal readonly struct PngChunkHeader
{
    public long Position { get; }
    public int Length { get; }
    public string Name { get; }
    public bool IsCritical => char.IsUpper(Name[0]);

    public PngChunkHeader(long position, int length, string name)
    {
        if (length < 0)
            throw new ArgumentException($"Length less than zero ({length}) encountered when reading chunk at position {position}.");

        Position = position;
        Length = length;
        Name = name;
    }
}
