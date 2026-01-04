using System;
using System.Collections.Generic;

namespace TinyImage.Codecs.Gif;

/// <summary>
/// Decodes GIF file format data.
/// Adapted from UniGif (MIT License).
/// </summary>
internal static class GifDecoder
{
    /// <summary>
    /// Parses a GIF file from byte data.
    /// </summary>
    /// <param name="gifBytes">The raw GIF file bytes.</param>
    /// <returns>The parsed GIF data structure.</returns>
    public static GifData Parse(byte[] gifBytes)
    {
        if (gifBytes == null || gifBytes.Length == 0)
            throw new ArgumentException("GIF data is empty.", nameof(gifBytes));

        var gifData = new GifData();
        int byteIndex = 0;

        // Parse header
        ParseHeader(gifBytes, ref byteIndex, ref gifData);

        // Parse blocks
        ParseBlocks(gifBytes, ref byteIndex, ref gifData);

        return gifData;
    }

    private static void ParseHeader(byte[] gifBytes, ref int byteIndex, ref GifData gifData)
    {
        // Signature (3 bytes): "GIF"
        if (gifBytes[0] != 'G' || gifBytes[1] != 'I' || gifBytes[2] != 'F')
            throw new InvalidOperationException("Invalid GIF signature.");

        gifData.Sig0 = gifBytes[0];
        gifData.Sig1 = gifBytes[1];
        gifData.Sig2 = gifBytes[2];

        // Version (3 bytes): "87a" or "89a"
        if ((gifBytes[3] != '8' || gifBytes[4] != '7' || gifBytes[5] != 'a') &&
            (gifBytes[3] != '8' || gifBytes[4] != '9' || gifBytes[5] != 'a'))
            throw new InvalidOperationException("Unsupported GIF version. Only GIF87a and GIF89a are supported.");

        gifData.Ver0 = gifBytes[3];
        gifData.Ver1 = gifBytes[4];
        gifData.Ver2 = gifBytes[5];

        // Logical Screen Width (2 bytes)
        gifData.LogicalScreenWidth = BitConverter.ToUInt16(gifBytes, 6);

        // Logical Screen Height (2 bytes)
        gifData.LogicalScreenHeight = BitConverter.ToUInt16(gifBytes, 8);

        // Packed byte
        byte packed = gifBytes[10];
        gifData.GlobalColorTableFlag = (packed & 0x80) != 0;
        gifData.ColorResolution = ((packed & 0x70) >> 4) + 1;
        gifData.SortFlag = (packed & 0x08) != 0;
        gifData.GlobalColorTableSize = 1 << ((packed & 0x07) + 1);

        // Background Color Index
        gifData.BackgroundColorIndex = gifBytes[11];

        // Pixel Aspect Ratio
        gifData.PixelAspectRatio = gifBytes[12];

        byteIndex = 13;

        // Global Color Table
        if (gifData.GlobalColorTableFlag)
        {
            gifData.GlobalColorTable = new List<byte[]>();
            for (int i = 0; i < gifData.GlobalColorTableSize; i++)
            {
                gifData.GlobalColorTable.Add(new byte[]
                {
                    gifBytes[byteIndex],
                    gifBytes[byteIndex + 1],
                    gifBytes[byteIndex + 2]
                });
                byteIndex += 3;
            }
        }
    }

    private static void ParseBlocks(byte[] gifBytes, ref int byteIndex, ref GifData gifData)
    {
        while (byteIndex < gifBytes.Length)
        {
            byte blockType = gifBytes[byteIndex];

            switch (blockType)
            {
                case 0x2C: // Image Descriptor
                    ParseImageBlock(gifBytes, ref byteIndex, ref gifData);
                    break;

                case 0x21: // Extension
                    if (byteIndex + 1 >= gifBytes.Length)
                        return;

                    byte extensionType = gifBytes[byteIndex + 1];
                    switch (extensionType)
                    {
                        case 0xF9: // Graphic Control Extension
                            ParseGraphicControlExtension(gifBytes, ref byteIndex, ref gifData);
                            break;
                        case 0xFF: // Application Extension
                            ParseApplicationExtension(gifBytes, ref byteIndex, ref gifData);
                            break;
                        case 0xFE: // Comment Extension
                        case 0x01: // Plain Text Extension
                            SkipExtension(gifBytes, ref byteIndex);
                            break;
                        default:
                            SkipExtension(gifBytes, ref byteIndex);
                            break;
                    }
                    break;

                case 0x3B: // Trailer
                    gifData.Trailer = gifBytes[byteIndex];
                    byteIndex++;
                    return;

                default:
                    // Unknown block, try to skip
                    byteIndex++;
                    break;
            }
        }
    }

    private static void ParseImageBlock(byte[] gifBytes, ref int byteIndex, ref GifData gifData)
    {
        var imageBlock = new GifImageBlock
        {
            ImageSeparator = gifBytes[byteIndex]
        };
        byteIndex++;

        // Image position and size
        imageBlock.ImageLeftPosition = BitConverter.ToUInt16(gifBytes, byteIndex);
        byteIndex += 2;
        imageBlock.ImageTopPosition = BitConverter.ToUInt16(gifBytes, byteIndex);
        byteIndex += 2;
        imageBlock.ImageWidth = BitConverter.ToUInt16(gifBytes, byteIndex);
        byteIndex += 2;
        imageBlock.ImageHeight = BitConverter.ToUInt16(gifBytes, byteIndex);
        byteIndex += 2;

        // Packed byte
        byte packed = gifBytes[byteIndex];
        imageBlock.LocalColorTableFlag = (packed & 0x80) != 0;
        imageBlock.InterlaceFlag = (packed & 0x40) != 0;
        imageBlock.SortFlag = (packed & 0x20) != 0;
        imageBlock.LocalColorTableSize = 1 << ((packed & 0x07) + 1);
        byteIndex++;

        // Local Color Table
        if (imageBlock.LocalColorTableFlag)
        {
            imageBlock.LocalColorTable = new List<byte[]>();
            for (int i = 0; i < imageBlock.LocalColorTableSize; i++)
            {
                imageBlock.LocalColorTable.Add(new byte[]
                {
                    gifBytes[byteIndex],
                    gifBytes[byteIndex + 1],
                    gifBytes[byteIndex + 2]
                });
                byteIndex += 3;
            }
        }

        // LZW Minimum Code Size
        imageBlock.LzwMinimumCodeSize = gifBytes[byteIndex];
        byteIndex++;

        // Image Data Sub-blocks
        imageBlock.ImageDataBlocks = new List<GifImageDataBlock>();
        while (true)
        {
            byte blockSize = gifBytes[byteIndex];
            byteIndex++;

            if (blockSize == 0)
                break;

            var dataBlock = new GifImageDataBlock
            {
                BlockSize = blockSize,
                ImageData = new byte[blockSize]
            };
            Array.Copy(gifBytes, byteIndex, dataBlock.ImageData, 0, blockSize);
            byteIndex += blockSize;

            imageBlock.ImageDataBlocks.Add(dataBlock);
        }

        gifData.ImageBlocks ??= new List<GifImageBlock>();
        gifData.ImageBlocks.Add(imageBlock);
    }

    private static void ParseGraphicControlExtension(byte[] gifBytes, ref int byteIndex, ref GifData gifData)
    {
        var gcExt = new GifGraphicControlExtension
        {
            ExtensionIntroducer = gifBytes[byteIndex],
            GraphicControlLabel = gifBytes[byteIndex + 1],
            BlockSize = gifBytes[byteIndex + 2]
        };
        byteIndex += 3;

        // Packed byte
        byte packed = gifBytes[byteIndex];
        gcExt.DisposalMethod = (ushort)((packed & 0x1C) >> 2);
        gcExt.TransparentColorFlag = (packed & 0x01) != 0;
        byteIndex++;

        // Delay time
        gcExt.DelayTime = BitConverter.ToUInt16(gifBytes, byteIndex);
        byteIndex += 2;

        // Transparent color index
        gcExt.TransparentColorIndex = gifBytes[byteIndex];
        byteIndex++;

        // Block terminator
        gcExt.BlockTerminator = gifBytes[byteIndex];
        byteIndex++;

        gifData.GraphicControlExtensions ??= new List<GifGraphicControlExtension>();
        gifData.GraphicControlExtensions.Add(gcExt);
    }

    private static void ParseApplicationExtension(byte[] gifBytes, ref int byteIndex, ref GifData gifData)
    {
        gifData.ApplicationExtension.ExtensionIntroducer = gifBytes[byteIndex];
        gifData.ApplicationExtension.ExtensionLabel = gifBytes[byteIndex + 1];
        gifData.ApplicationExtension.BlockSize = gifBytes[byteIndex + 2];
        byteIndex += 3;

        // Application Identifier (8 bytes)
        gifData.ApplicationExtension.AppId1 = gifBytes[byteIndex++];
        gifData.ApplicationExtension.AppId2 = gifBytes[byteIndex++];
        gifData.ApplicationExtension.AppId3 = gifBytes[byteIndex++];
        gifData.ApplicationExtension.AppId4 = gifBytes[byteIndex++];
        gifData.ApplicationExtension.AppId5 = gifBytes[byteIndex++];
        gifData.ApplicationExtension.AppId6 = gifBytes[byteIndex++];
        gifData.ApplicationExtension.AppId7 = gifBytes[byteIndex++];
        gifData.ApplicationExtension.AppId8 = gifBytes[byteIndex++];

        // Application Authentication Code (3 bytes)
        gifData.ApplicationExtension.AppAuthCode1 = gifBytes[byteIndex++];
        gifData.ApplicationExtension.AppAuthCode2 = gifBytes[byteIndex++];
        gifData.ApplicationExtension.AppAuthCode3 = gifBytes[byteIndex++];

        // Application Data Sub-blocks
        gifData.ApplicationExtension.AppDataBlocks = new List<GifApplicationDataBlock>();
        while (true)
        {
            byte blockSize = gifBytes[byteIndex];
            byteIndex++;

            if (blockSize == 0)
                break;

            var dataBlock = new GifApplicationDataBlock
            {
                BlockSize = blockSize,
                ApplicationData = new byte[blockSize]
            };
            Array.Copy(gifBytes, byteIndex, dataBlock.ApplicationData, 0, blockSize);
            byteIndex += blockSize;

            gifData.ApplicationExtension.AppDataBlocks.Add(dataBlock);
        }
    }

    private static void SkipExtension(byte[] gifBytes, ref int byteIndex)
    {
        byteIndex += 2; // Skip introducer and label

        // Skip sub-blocks
        while (byteIndex < gifBytes.Length)
        {
            byte blockSize = gifBytes[byteIndex];
            byteIndex++;

            if (blockSize == 0)
                break;

            byteIndex += blockSize;
        }
    }

    /// <summary>
    /// Decodes image data from a GIF image block.
    /// </summary>
    public static byte[] DecodeImageData(GifImageBlock imageBlock)
    {
        // Combine all data blocks
        var compressedData = new List<byte>();
        if (imageBlock.ImageDataBlocks != null)
        {
            foreach (var block in imageBlock.ImageDataBlocks)
            {
                compressedData.AddRange(block.ImageData);
            }
        }

        // Decompress using LZW
        int expectedSize = imageBlock.ImageWidth * imageBlock.ImageHeight;
        var decodedData = LzwDecoder.Decode(compressedData, imageBlock.LzwMinimumCodeSize, expectedSize);

        // Handle interlacing
        if (imageBlock.InterlaceFlag)
        {
            decodedData = LzwDecoder.SortInterlacedData(decodedData, imageBlock.ImageWidth, imageBlock.ImageHeight);
        }

        return decodedData;
    }

    /// <summary>
    /// Gets the color table for an image block (local or global).
    /// </summary>
    public static List<byte[]>? GetColorTable(GifData gifData, GifImageBlock imageBlock)
    {
        return imageBlock.LocalColorTableFlag 
            ? imageBlock.LocalColorTable 
            : gifData.GlobalColorTable;
    }
}
