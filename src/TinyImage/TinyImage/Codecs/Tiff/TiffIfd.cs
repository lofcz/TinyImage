using System;
using System.Collections.Generic;

namespace TinyImage.Codecs.Tiff;

/// <summary>
/// Represents an IFD (Image File Directory) in a TIFF file.
/// Based on image-tiff directory.rs.
/// </summary>
internal sealed class TiffIfd
{
    private readonly Dictionary<TiffTag, TiffIfdEntry> _entries;

    /// <summary>
    /// Creates an empty IFD.
    /// </summary>
    public TiffIfd()
    {
        _entries = new Dictionary<TiffTag, TiffIfdEntry>();
    }

    /// <summary>
    /// Gets the entries in this IFD.
    /// </summary>
    public IReadOnlyDictionary<TiffTag, TiffIfdEntry> Entries => _entries;

    /// <summary>
    /// Gets or sets the offset to the next IFD (0 if this is the last IFD).
    /// </summary>
    public long NextIfdOffset { get; set; }

    /// <summary>
    /// Adds an entry to this IFD.
    /// </summary>
    public void AddEntry(TiffIfdEntry entry)
    {
        _entries[entry.Tag] = entry;
    }

    /// <summary>
    /// Gets whether this IFD contains the specified tag.
    /// </summary>
    public bool ContainsTag(TiffTag tag) => _entries.ContainsKey(tag);

    /// <summary>
    /// Tries to get an entry for the specified tag.
    /// </summary>
    public bool TryGetEntry(TiffTag tag, out TiffIfdEntry entry) => _entries.TryGetValue(tag, out entry);

    /// <summary>
    /// Gets the entry for the specified tag.
    /// </summary>
    /// <exception cref="TiffFormatException">Tag not found.</exception>
    public TiffIfdEntry GetEntry(TiffTag tag)
    {
        if (!_entries.TryGetValue(tag, out var entry))
            throw new TiffFormatException($"Required tag {tag} not found.");
        return entry;
    }

    /// <summary>
    /// Gets a required unsigned integer value for a tag.
    /// </summary>
    public uint GetRequiredUInt32(TiffTag tag) => GetEntry(tag).GetUInt32Value();

    /// <summary>
    /// Gets an optional unsigned integer value for a tag.
    /// </summary>
    public uint? GetOptionalUInt32(TiffTag tag)
    {
        return _entries.TryGetValue(tag, out var entry) ? entry.GetUInt32Value() : null;
    }

    /// <summary>
    /// Gets an optional unsigned 16-bit value for a tag.
    /// </summary>
    public ushort? GetOptionalUInt16(TiffTag tag)
    {
        return _entries.TryGetValue(tag, out var entry) ? entry.GetUInt16Value() : null;
    }

    /// <summary>
    /// Gets the number of entries in this IFD.
    /// </summary>
    public int Count => _entries.Count;
}
