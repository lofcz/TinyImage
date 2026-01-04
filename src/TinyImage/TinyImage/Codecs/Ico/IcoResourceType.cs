namespace TinyImage.Codecs.Ico;

/// <summary>
/// The type of resource stored in an ICO/CUR file.
/// </summary>
public enum IcoResourceType : ushort
{
    /// <summary>
    /// Plain images (ICO files).
    /// </summary>
    Icon = 1,

    /// <summary>
    /// Images with cursor hotspots (CUR files).
    /// </summary>
    Cursor = 2
}
