using System.Collections.Generic;

namespace TinyImage.Codecs.Gif;

/// <summary>
/// Represents the complete data structure of a GIF file.
/// </summary>
internal struct GifData
{
    // Header
    public byte Sig0, Sig1, Sig2;           // Signature "GIF"
    public byte Ver0, Ver1, Ver2;           // Version "87a" or "89a"
    
    // Logical Screen Descriptor
    public ushort LogicalScreenWidth;
    public ushort LogicalScreenHeight;
    public bool GlobalColorTableFlag;
    public int ColorResolution;
    public bool SortFlag;
    public int GlobalColorTableSize;
    public byte BackgroundColorIndex;
    public byte PixelAspectRatio;
    
    // Global Color Table
    public List<byte[]>? GlobalColorTable;
    
    // Image Blocks
    public List<GifImageBlock>? ImageBlocks;
    
    // Extensions
    public List<GifGraphicControlExtension>? GraphicControlExtensions;
    public GifApplicationExtension ApplicationExtension;
    
    // Trailer
    public byte Trailer;

    public readonly string Signature => new string(new[] { (char)Sig0, (char)Sig1, (char)Sig2 });
    public readonly string Version => new string(new[] { (char)Ver0, (char)Ver1, (char)Ver2 });
}

/// <summary>
/// Represents a single image block (frame) in a GIF.
/// </summary>
internal struct GifImageBlock
{
    public byte ImageSeparator;             // Always 0x2C
    public ushort ImageLeftPosition;
    public ushort ImageTopPosition;
    public ushort ImageWidth;
    public ushort ImageHeight;
    public bool LocalColorTableFlag;
    public bool InterlaceFlag;
    public bool SortFlag;
    public int LocalColorTableSize;
    public List<byte[]>? LocalColorTable;
    public byte LzwMinimumCodeSize;
    public List<GifImageDataBlock>? ImageDataBlocks;
}

/// <summary>
/// Represents a sub-block of image data.
/// </summary>
internal struct GifImageDataBlock
{
    public byte BlockSize;
    public byte[] ImageData;
}

/// <summary>
/// Represents the Graphic Control Extension for animation timing and transparency.
/// </summary>
internal struct GifGraphicControlExtension
{
    public byte ExtensionIntroducer;        // Always 0x21
    public byte GraphicControlLabel;        // Always 0xF9
    public byte BlockSize;                  // Always 0x04
    public ushort DisposalMethod;           // 0=unspecified, 1=none, 2=restore bg, 3=restore previous
    public bool TransparentColorFlag;
    public ushort DelayTime;                // In hundredths of a second
    public byte TransparentColorIndex;
    public byte BlockTerminator;            // Always 0x00
}

/// <summary>
/// Represents the Application Extension (used for NETSCAPE looping).
/// </summary>
internal struct GifApplicationExtension
{
    public byte ExtensionIntroducer;        // Always 0x21
    public byte ExtensionLabel;             // Always 0xFF
    public byte BlockSize;                  // Always 0x0B
    public byte AppId1, AppId2, AppId3, AppId4, AppId5, AppId6, AppId7, AppId8;
    public byte AppAuthCode1, AppAuthCode2, AppAuthCode3;
    public List<GifApplicationDataBlock>? AppDataBlocks;

    public readonly string ApplicationIdentifier => new string(new[] 
    { 
        (char)AppId1, (char)AppId2, (char)AppId3, (char)AppId4, 
        (char)AppId5, (char)AppId6, (char)AppId7, (char)AppId8 
    });

    public readonly string ApplicationAuthCode => new string(new[] 
    { 
        (char)AppAuthCode1, (char)AppAuthCode2, (char)AppAuthCode3 
    });

    public readonly int LoopCount
    {
        get
        {
            if (AppDataBlocks == null || AppDataBlocks.Count < 1 ||
                AppDataBlocks[0].ApplicationData.Length < 3 ||
                AppDataBlocks[0].ApplicationData[0] != 0x01)
            {
                return 0;
            }
            return AppDataBlocks[0].ApplicationData[1] | (AppDataBlocks[0].ApplicationData[2] << 8);
        }
    }
}

/// <summary>
/// Represents a sub-block of application data.
/// </summary>
internal struct GifApplicationDataBlock
{
    public byte BlockSize;
    public byte[] ApplicationData;
}
