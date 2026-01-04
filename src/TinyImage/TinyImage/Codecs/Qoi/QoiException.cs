using System;

namespace TinyImage.Codecs.Qoi;

/// <summary>
/// Exception thrown when an error occurs during QOI encoding or decoding.
/// </summary>
public class QoiException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="QoiException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public QoiException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="QoiException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public QoiException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when QOI decoding fails.
/// </summary>
public class QoiDecodingException : QoiException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="QoiDecodingException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public QoiDecodingException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="QoiDecodingException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public QoiDecodingException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when QOI encoding fails.
/// </summary>
public class QoiEncodingException : QoiException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="QoiEncodingException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public QoiEncodingException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="QoiEncodingException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public QoiEncodingException(string message, Exception innerException) : base(message, innerException) { }
}
