using System;

namespace TinyImage.Codecs.Tiff;

/// <summary>
/// Exception thrown when TIFF encoding or decoding fails.
/// </summary>
public class TiffException : Exception
{
    /// <summary>
    /// Creates a new TiffException with the specified message.
    /// </summary>
    public TiffException(string message) : base(message)
    {
    }

    /// <summary>
    /// Creates a new TiffException with the specified message and inner exception.
    /// </summary>
    public TiffException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when a TIFF file has an invalid format.
/// </summary>
internal class TiffFormatException : TiffException
{
    public TiffFormatException(string message) : base(message)
    {
    }
}

/// <summary>
/// Exception thrown when a TIFF file uses an unsupported feature.
/// </summary>
internal class TiffUnsupportedException : TiffException
{
    public TiffUnsupportedException(string message) : base(message)
    {
    }
}
