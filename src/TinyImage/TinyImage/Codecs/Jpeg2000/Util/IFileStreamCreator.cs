// Copyright (c) 2007-2016 CSJ2K contributors.
// Licensed under the BSD 3-Clause License.

namespace TinyImage.Codecs.Jpeg2000.Util
{
    using System.IO;

    internal interface IFileStreamCreator
    {
        Stream Create(string path, string mode);
    }
}
