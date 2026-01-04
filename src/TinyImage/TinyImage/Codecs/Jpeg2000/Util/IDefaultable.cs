// Copyright (c) 2012-2016 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).

namespace TinyImage.Codecs.Jpeg2000.Util
{
    /// <summary>
    /// Interface for default classification of manager types.
    /// </summary>
    internal interface IDefaultable
    {
        /// <summary>
        /// Gets whether or not this type is classified as a default manager.
        /// </summary>
        bool IsDefault { get; }
    }
}