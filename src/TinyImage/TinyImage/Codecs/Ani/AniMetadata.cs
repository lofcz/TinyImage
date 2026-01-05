using System;
using System.Collections.Generic;

namespace TinyImage.Codecs.Ani;

/// <summary>
/// ANI-specific metadata for an animated cursor image.
/// </summary>
/// <remarks>
/// Access via <c>image.GetMetadata&lt;AniMetadata&gt;()</c> after loading an ANI file.
/// Set via <c>image.SetMetadata(new AniMetadata { ... })</c> before saving.
/// </remarks>
public sealed class AniMetadata : IMetadata, ICloneableMetadata
{
    /// <summary>
    /// Gets or sets the animation title (from INFO/INAM chunk).
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the animation author (from INFO/IART chunk).
    /// </summary>
    public string? Author { get; set; }

    /// <summary>
    /// Gets or sets the default display rate in jiffies (1/60 second).
    /// Default is 10 (approximately 6 fps).
    /// </summary>
    public uint DefaultDisplayRate { get; set; } = 10;

    /// <summary>
    /// Gets the per-step display rates in jiffies.
    /// If null or empty, DefaultDisplayRate is used for all steps.
    /// </summary>
    public List<uint>? Rates { get; set; }

    /// <summary>
    /// Gets the sequence of frame indices for animation.
    /// If null or empty, frames are played in order (0, 1, 2, ...).
    /// </summary>
    public List<uint>? Sequence { get; set; }

    /// <summary>
    /// Gets or sets the hotspots for each unique frame.
    /// </summary>
    public List<(ushort X, ushort Y)>? Hotspots { get; set; }

    /// <summary>
    /// Gets the display rate for a specific step in jiffies.
    /// </summary>
    public uint GetStepRate(int stepIndex)
    {
        if (Rates != null && stepIndex >= 0 && stepIndex < Rates.Count)
            return Rates[stepIndex];
        return DefaultDisplayRate;
    }

    /// <summary>
    /// Gets the frame index for a specific step.
    /// </summary>
    public uint GetStepFrameIndex(int stepIndex, int totalFrames)
    {
        if (Sequence != null && stepIndex >= 0 && stepIndex < Sequence.Count)
            return Sequence[stepIndex];
        return (uint)(stepIndex % totalFrames);
    }

    /// <summary>
    /// Sets the display rate for a specific step.
    /// </summary>
    public void SetStepRate(int stepIndex, uint rateJiffies)
    {
        Rates ??= new List<uint>();
        while (Rates.Count <= stepIndex)
            Rates.Add(DefaultDisplayRate);
        Rates[stepIndex] = rateJiffies;
    }

    /// <summary>
    /// Sets the hotspot for a specific frame.
    /// </summary>
    public void SetHotspot(int frameIndex, ushort x, ushort y)
    {
        Hotspots ??= new List<(ushort, ushort)>();
        while (Hotspots.Count <= frameIndex)
            Hotspots.Add((0, 0));
        Hotspots[frameIndex] = (x, y);
    }

    /// <summary>
    /// Converts jiffies (1/60s) to milliseconds.
    /// </summary>
    public static double JiffiesToMilliseconds(uint jiffies)
    {
        return jiffies * (1000.0 / 60.0);
    }

    /// <summary>
    /// Converts milliseconds to jiffies (1/60s).
    /// </summary>
    public static uint MillisecondsToJiffies(double milliseconds)
    {
        return (uint)(milliseconds * 60.0 / 1000.0 + 0.5);
    }

    /// <inheritdoc/>
    public void OnImageResized(int oldWidth, int oldHeight, int newWidth, int newHeight)
    {
        if (Hotspots == null || Hotspots.Count == 0)
            return;

        double xScale = (double)newWidth / oldWidth;
        double yScale = (double)newHeight / oldHeight;

        for (int i = 0; i < Hotspots.Count; i++)
        {
            var (x, y) = Hotspots[i];
            ushort newX = (ushort)Math.Round(x * xScale);
            ushort newY = (ushort)Math.Round(y * yScale);
            Hotspots[i] = (newX, newY);
        }
    }

    /// <inheritdoc/>
    public object Clone()
    {
        return new AniMetadata
        {
            Title = Title,
            Author = Author,
            DefaultDisplayRate = DefaultDisplayRate,
            Rates = Rates != null ? new List<uint>(Rates) : null,
            Sequence = Sequence != null ? new List<uint>(Sequence) : null,
            Hotspots = Hotspots != null ? new List<(ushort X, ushort Y)>(Hotspots) : null
        };
    }
}
