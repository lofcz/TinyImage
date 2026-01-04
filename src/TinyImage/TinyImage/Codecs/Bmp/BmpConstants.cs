namespace TinyImage.Codecs.Bmp;

/// <summary>
/// Defines constants relating to BMP files.
/// </summary>
internal static class BmpConstants
{
    /// <summary>
    /// The list of MIME types that equate to a BMP.
    /// </summary>
    public static readonly string[] MimeTypes =
    [
        "image/bmp",
        "image/x-windows-bmp",
        "image/x-win-bitmap"
    ];

    /// <summary>
    /// The list of file extensions that equate to a BMP.
    /// </summary>
    public static readonly string[] FileExtensions = ["bmp", "dib"];

    /// <summary>
    /// Valid magic bytes markers identifying a Bitmap file.
    /// </summary>
    public static class TypeMarkers
    {
        /// <summary>
        /// Single-image BMP file that may have been created under Windows or OS/2.
        /// ASCII "BM" = 0x42 0x4D (stored as little-endian 0x4D42).
        /// </summary>
        public const ushort Bitmap = 0x4D42;

        /// <summary>
        /// OS/2 Bitmap Array ("BA").
        /// </summary>
        public const ushort BitmapArray = 0x4142;

        /// <summary>
        /// OS/2 Color Icon ("CI").
        /// </summary>
        public const ushort ColorIcon = 0x4943;

        /// <summary>
        /// OS/2 Color Pointer ("CP").
        /// </summary>
        public const ushort ColorPointer = 0x5043;

        /// <summary>
        /// OS/2 Icon ("IC").
        /// </summary>
        public const ushort Icon = 0x4349;

        /// <summary>
        /// OS/2 Pointer ("PT").
        /// </summary>
        public const ushort Pointer = 0x5450;
    }

    /// <summary>
    /// Header size constants.
    /// </summary>
    public static class HeaderSizes
    {
        /// <summary>
        /// Size of the BMP file header in bytes.
        /// </summary>
        public const int FileHeader = 14;

        /// <summary>
        /// Size of BITMAPCOREHEADER (BMP Version 2).
        /// </summary>
        public const int CoreHeader = 12;

        /// <summary>
        /// Size of short variant OS/2 2.x header.
        /// </summary>
        public const int Os22ShortHeader = 16;

        /// <summary>
        /// Size of BITMAPINFOHEADER (BMP Version 3).
        /// </summary>
        public const int InfoHeaderV3 = 40;

        /// <summary>
        /// Adobe variant V3 with RGB masks.
        /// </summary>
        public const int AdobeV3Header = 52;

        /// <summary>
        /// Adobe variant V3 with RGBA masks.
        /// </summary>
        public const int AdobeV3WithAlphaHeader = 56;

        /// <summary>
        /// Size of OS/2 2.x header.
        /// </summary>
        public const int Os2V2Header = 64;

        /// <summary>
        /// Size of BITMAPV4HEADER.
        /// </summary>
        public const int InfoHeaderV4 = 108;

        /// <summary>
        /// Size of BITMAPV5HEADER.
        /// </summary>
        public const int InfoHeaderV5 = 124;
    }
}
