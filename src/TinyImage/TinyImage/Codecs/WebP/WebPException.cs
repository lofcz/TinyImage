using System;

namespace TinyImage.Codecs.WebP;

/// <summary>
/// Exception thrown when an error occurs during WebP encoding or decoding.
/// </summary>
public class WebPException : Exception
{
    public WebPException(string message) : base(message) { }
    public WebPException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when WebP decoding fails.
/// </summary>
public class WebPDecodingException : WebPException
{
    public WebPDecodingException(string message) : base(message) { }
    public WebPDecodingException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when WebP encoding fails.
/// </summary>
public class WebPEncodingException : WebPException
{
    public WebPEncodingException(string message) : base(message) { }
    public WebPEncodingException(string message, Exception innerException) : base(message, innerException) { }
}
