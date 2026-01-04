namespace TinyImage.Codecs.Tiff;

/// <summary>
/// TIFF tag identifiers.
/// Based on image-tiff tags.rs lines 68-143.
/// </summary>
internal enum TiffTag : ushort
{
    // Baseline tags
    
    /// <summary>
    /// A general indication of the kind of data contained in this subfile.
    /// </summary>
    NewSubfileType = 254,

    /// <summary>
    /// A general indication of the kind of data contained in this subfile (old-style).
    /// </summary>
    SubfileType = 255,

    /// <summary>
    /// The number of columns in the image.
    /// </summary>
    ImageWidth = 256,

    /// <summary>
    /// The number of rows in the image.
    /// </summary>
    ImageLength = 257,

    /// <summary>
    /// Number of bits per component.
    /// </summary>
    BitsPerSample = 258,

    /// <summary>
    /// Compression scheme used on the image data.
    /// </summary>
    Compression = 259,

    /// <summary>
    /// The color space of the image data.
    /// </summary>
    PhotometricInterpretation = 262,

    /// <summary>
    /// For bilevel images, specifies the width of the dithering matrix.
    /// </summary>
    Threshholding = 263,

    /// <summary>
    /// The width of the dithering or halftoning matrix.
    /// </summary>
    CellWidth = 264,

    /// <summary>
    /// The length of the dithering or halftoning matrix.
    /// </summary>
    CellLength = 265,

    /// <summary>
    /// The logical order of bits within a byte.
    /// </summary>
    FillOrder = 266,

    /// <summary>
    /// A string that describes the subject of the image.
    /// </summary>
    ImageDescription = 270,

    /// <summary>
    /// The scanner manufacturer.
    /// </summary>
    Make = 271,

    /// <summary>
    /// The scanner model name or number.
    /// </summary>
    Model = 272,

    /// <summary>
    /// For each strip, the byte offset of that strip.
    /// </summary>
    StripOffsets = 273,

    /// <summary>
    /// The orientation of the image with respect to the rows and columns.
    /// </summary>
    Orientation = 274,

    /// <summary>
    /// The number of components per pixel.
    /// </summary>
    SamplesPerPixel = 277,

    /// <summary>
    /// The number of rows per strip.
    /// </summary>
    RowsPerStrip = 278,

    /// <summary>
    /// For each strip, the number of bytes in the strip after compression.
    /// </summary>
    StripByteCounts = 279,

    /// <summary>
    /// The minimum component value used.
    /// </summary>
    MinSampleValue = 280,

    /// <summary>
    /// The maximum component value used.
    /// </summary>
    MaxSampleValue = 281,

    /// <summary>
    /// The number of pixels per ResolutionUnit in the ImageWidth direction.
    /// </summary>
    XResolution = 282,

    /// <summary>
    /// The number of pixels per ResolutionUnit in the ImageLength direction.
    /// </summary>
    YResolution = 283,

    /// <summary>
    /// How the components of each pixel are stored.
    /// </summary>
    PlanarConfiguration = 284,

    /// <summary>
    /// For each string of contiguous unused bytes, the byte offset.
    /// </summary>
    FreeOffsets = 288,

    /// <summary>
    /// For each string of contiguous unused bytes, the number of bytes.
    /// </summary>
    FreeByteCounts = 289,

    /// <summary>
    /// The precision of the information contained in the GrayResponseCurve.
    /// </summary>
    GrayResponseUnit = 290,

    /// <summary>
    /// For grayscale data, the optical density of each possible pixel value.
    /// </summary>
    GrayResponseCurve = 291,

    /// <summary>
    /// The unit of measurement for XResolution and YResolution.
    /// </summary>
    ResolutionUnit = 296,

    /// <summary>
    /// Name and version number of the software package(s) used to create the image.
    /// </summary>
    Software = 305,

    /// <summary>
    /// Date and time of image creation.
    /// </summary>
    DateTime = 306,

    /// <summary>
    /// Person who created the image.
    /// </summary>
    Artist = 315,

    /// <summary>
    /// The computer and/or operating system in use at the time of image creation.
    /// </summary>
    HostComputer = 316,

    /// <summary>
    /// A mathematical operator applied to the image data before compression.
    /// </summary>
    Predictor = 317,

    /// <summary>
    /// A color map for palette color images.
    /// </summary>
    ColorMap = 320,

    /// <summary>
    /// The tile width in pixels.
    /// </summary>
    TileWidth = 322,

    /// <summary>
    /// The tile length (height) in pixels.
    /// </summary>
    TileLength = 323,

    /// <summary>
    /// For each tile, the byte offset of that tile.
    /// </summary>
    TileOffsets = 324,

    /// <summary>
    /// For each tile, the number of bytes in that tile.
    /// </summary>
    TileByteCounts = 325,

    /// <summary>
    /// Offsets to child IFDs.
    /// </summary>
    SubIfd = 330,

    /// <summary>
    /// Description of extra components.
    /// </summary>
    ExtraSamples = 338,

    /// <summary>
    /// Specifies how to interpret each data sample in a pixel.
    /// </summary>
    SampleFormat = 339,

    /// <summary>
    /// Minimum sample value.
    /// </summary>
    SMinSampleValue = 340,

    /// <summary>
    /// Maximum sample value.
    /// </summary>
    SMaxSampleValue = 341,

    /// <summary>
    /// JPEG quantization and/or Huffman tables.
    /// </summary>
    JpegTables = 347,

    /// <summary>
    /// YCbCr subsampling factors.
    /// </summary>
    YCbCrSubSampling = 530,

    /// <summary>
    /// YCbCr positioning.
    /// </summary>
    YCbCrPositioning = 531,

    /// <summary>
    /// YCbCr coefficients (LumaRed, LumaGreen, LumaBlue).
    /// </summary>
    YCbCrCoefficients = 529,

    /// <summary>
    /// Reference black/white values.
    /// </summary>
    ReferenceBlackWhite = 532,

    /// <summary>
    /// Copyright notice.
    /// </summary>
    Copyright = 33432,

    /// <summary>
    /// Exif IFD pointer.
    /// </summary>
    ExifIfd = 34665,

    /// <summary>
    /// GPS IFD pointer.
    /// </summary>
    GpsIfd = 34853,

    /// <summary>
    /// ICC profile data.
    /// </summary>
    IccProfile = 34675,

    // GeoTIFF tags
    
    /// <summary>
    /// GeoTIFF model pixel scale.
    /// </summary>
    ModelPixelScale = 33550,

    /// <summary>
    /// GeoTIFF model transformation.
    /// </summary>
    ModelTransformation = 34264,

    /// <summary>
    /// GeoTIFF model tiepoint.
    /// </summary>
    ModelTiepoint = 33922,

    /// <summary>
    /// GeoTIFF key directory.
    /// </summary>
    GeoKeyDirectory = 34735,

    /// <summary>
    /// GeoTIFF double params.
    /// </summary>
    GeoDoubleParams = 34736,

    /// <summary>
    /// GeoTIFF ASCII params.
    /// </summary>
    GeoAsciiParams = 34737
}
