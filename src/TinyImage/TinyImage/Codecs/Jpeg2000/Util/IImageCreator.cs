// Copyright (c) 2007-2016 CSJ2K contributors.
// Licensed under the BSD 3-Clause License.

namespace TinyImage.Codecs.Jpeg2000.Util
{
    using j2k.image;

    internal interface IImageCreator
    {
        IImage Create(int width, int height, int numComponents, byte[] bytes);

        BlkImgDataSrc ToPortableImageSource(object imageObject);

        System.Type ImageType { get; }
    }
}
