using System;

namespace TinyImage.Codecs.Ani;

/// <summary>
/// ANI file header flags.
/// </summary>
[Flags]
internal enum AniFlags : uint
{
    /// <summary>
    /// No flags set.
    /// </summary>
    None = 0,

    /// <summary>
    /// Frames are in Windows ICO/CUR format (not raw BMP).
    /// </summary>
    Icon = 0x1,

    /// <summary>
    /// Animation uses a sequence table.
    /// </summary>
    Sequence = 0x2
}

/// <summary>
/// ANI file header structure (36 bytes).
/// </summary>
internal struct AniHeader
{
    /// <summary>
    /// Size of this structure (should be 36).
    /// </summary>
    public uint HeaderSize;

    /// <summary>
    /// Number of unique frames stored in the file.
    /// </summary>
    public uint NumFrames;

    /// <summary>
    /// Number of steps in the animation sequence.
    /// May differ from NumFrames if sequence data is present.
    /// </summary>
    public uint NumSteps;

    /// <summary>
    /// Width (not used, should be 0).
    /// </summary>
    public uint Width;

    /// <summary>
    /// Height (not used, should be 0).
    /// </summary>
    public uint Height;

    /// <summary>
    /// Bit count (not used, should be 0).
    /// </summary>
    public uint BitCount;

    /// <summary>
    /// Number of planes (not used, should be 1).
    /// </summary>
    public uint NumPlanes;

    /// <summary>
    /// Default display rate in jiffies (1/60 second).
    /// </summary>
    public uint DisplayRate;

    /// <summary>
    /// Flags (Icon and/or Sequence).
    /// </summary>
    public AniFlags Flags;

    /// <summary>
    /// Gets whether the Icon flag is set.
    /// </summary>
    public bool HasIconFlag => (Flags & AniFlags.Icon) != 0;

    /// <summary>
    /// Gets whether the Sequence flag is set.
    /// </summary>
    public bool HasSequenceFlag => (Flags & AniFlags.Sequence) != 0;

    /// <summary>
    /// Expected header size.
    /// </summary>
    public const uint ExpectedSize = 36;
}
