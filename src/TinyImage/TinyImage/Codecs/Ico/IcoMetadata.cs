using System.Collections.Generic;

namespace TinyImage.Codecs.Ico;

/// <summary>
/// ICO/CUR-specific metadata for an image.
/// </summary>
/// <remarks>
/// Access via <c>image.GetMetadata&lt;IcoMetadata&gt;()</c> after loading an ICO/CUR file.
/// Set via <c>image.SetMetadata(new IcoMetadata { ... })</c> before saving.
/// </remarks>
public sealed class IcoMetadata
{
    private readonly List<IcoEntryMetadata> _entries = new List<IcoEntryMetadata>();

    /// <summary>
    /// Gets or sets the resource type (Icon or Cursor).
    /// </summary>
    public IcoResourceType ResourceType { get; set; } = IcoResourceType.Icon;

    /// <summary>
    /// Gets the metadata for each entry/frame in the ICO/CUR file.
    /// </summary>
    public IReadOnlyList<IcoEntryMetadata> Entries => _entries;

    /// <summary>
    /// Adds metadata for an entry.
    /// </summary>
    internal void AddEntry(IcoEntryMetadata entry)
    {
        _entries.Add(entry);
    }

    /// <summary>
    /// Gets the metadata for a specific entry by index.
    /// </summary>
    /// <param name="index">The entry index (corresponding to frame index).</param>
    /// <returns>The entry metadata, or null if index is out of range.</returns>
    public IcoEntryMetadata? GetEntry(int index)
    {
        if (index >= 0 && index < _entries.Count)
            return _entries[index];
        return null;
    }

    /// <summary>
    /// Sets the cursor hotspot for a specific entry.
    /// </summary>
    /// <param name="entryIndex">The entry index.</param>
    /// <param name="x">Hotspot X coordinate (pixels from left).</param>
    /// <param name="y">Hotspot Y coordinate (pixels from top).</param>
    public void SetHotspot(int entryIndex, ushort x, ushort y)
    {
        while (_entries.Count <= entryIndex)
            _entries.Add(new IcoEntryMetadata());

        _entries[entryIndex].HotspotX = x;
        _entries[entryIndex].HotspotY = y;
        ResourceType = IcoResourceType.Cursor;
    }
}

/// <summary>
/// Metadata for a single entry in an ICO/CUR file.
/// </summary>
public sealed class IcoEntryMetadata
{
    /// <summary>
    /// Gets or sets the cursor hotspot X coordinate (pixels from left edge).
    /// Only meaningful for CUR files.
    /// </summary>
    public ushort HotspotX { get; set; }

    /// <summary>
    /// Gets or sets the cursor hotspot Y coordinate (pixels from top edge).
    /// Only meaningful for CUR files.
    /// </summary>
    public ushort HotspotY { get; set; }

    /// <summary>
    /// Gets or sets the bits per pixel of the original image.
    /// </summary>
    public ushort BitsPerPixel { get; set; }

    /// <summary>
    /// Gets or sets whether the original image was PNG encoded (vs BMP).
    /// </summary>
    public bool IsPng { get; set; }

    /// <summary>
    /// Gets the cursor hotspot as a tuple.
    /// </summary>
    public (ushort X, ushort Y) Hotspot => (HotspotX, HotspotY);
}
