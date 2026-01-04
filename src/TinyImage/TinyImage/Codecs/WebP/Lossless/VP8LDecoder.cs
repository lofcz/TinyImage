using System;
using System.Collections.Generic;
using System.IO;
using TinyImage.Codecs.WebP.Core;

namespace TinyImage.Codecs.WebP.Lossless;

/// <summary>
/// VP8L lossless WebP decoder.
/// Translated from webp-rust lossless.rs
/// </summary>
internal class VP8LDecoder
{
    private const int NumTransformTypes = 4;
    private const int HuffmanCodesPerMetaCode = 5;
    private const int Green = 0;
    private const int Red = 1;
    private const int Blue = 2;
    private const int Alpha = 3;
    private const int Dist = 4;

    private static readonly ushort[] AlphabetSize = { 256 + 24, 256, 256, 256, 40 };
    private static readonly int[] CodeLengthCodeOrder = {
        17, 18, 0, 1, 2, 3, 4, 5, 16, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15
    };

    private readonly BitReader _bitReader;
    private readonly LosslessTransform[] _transforms;
    private readonly List<byte> _transformOrder;
    private ushort _width;
    private ushort _height;

    public VP8LDecoder(Stream stream)
    {
        _bitReader = new BitReader(stream);
        _transforms = new LosslessTransform[NumTransformTypes];
        _transformOrder = new List<byte>();
    }

    public VP8LDecoder(byte[] data)
    {
        _bitReader = new BitReader(data);
        _transforms = new LosslessTransform[NumTransformTypes];
        _transformOrder = new List<byte>();
    }

    /// <summary>
    /// Decodes a VP8L frame.
    /// </summary>
    /// <param name="width">Expected image width</param>
    /// <param name="height">Expected image height</param>
    /// <param name="implicitDimensions">If true, dimensions are not read from header (used for alpha chunks)</param>
    /// <param name="buffer">Output buffer for RGBA data (must be width * height * 4 bytes)</param>
    public void DecodeFrame(uint width, uint height, bool implicitDimensions, byte[] buffer)
    {
        if (implicitDimensions)
        {
            _width = (ushort)width;
            _height = (ushort)height;
        }
        else
        {
            // Read VP8L header
            byte signature = (byte)_bitReader.ReadBits(8);
            if (signature != 0x2f)
                throw new WebPDecodingException($"Invalid VP8L signature: 0x{signature:X2}");

            _width = (ushort)(_bitReader.ReadBits(14) + 1);
            _height = (ushort)(_bitReader.ReadBits(14) + 1);

            if (_width != width || _height != height)
                throw new WebPDecodingException("Inconsistent image sizes");

            _ = _bitReader.ReadBits(1); // alpha_used
            uint versionNum = _bitReader.ReadBits(3);
            if (versionNum != 0)
                throw new WebPDecodingException($"Invalid VP8L version: {versionNum}");
        }

        ushort transformedWidth = ReadTransforms();
        int transformedSize = transformedWidth * _height * 4;

        DecodeImageStream(transformedWidth, _height, true, buffer, 0, transformedSize);

        // Apply transforms in reverse order
        int imageSize = transformedSize;
        ushort currentWidth = transformedWidth;

        for (int i = _transformOrder.Count - 1; i >= 0; i--)
        {
            var transform = _transforms[_transformOrder[i]];
            switch (transform.Kind)
            {
                case TransformKind.Predictor:
                    LosslessTransformApply.ApplyPredictorTransform(
                        buffer, currentWidth, _height, transform.SizeBits, transform.Data);
                    break;

                case TransformKind.Color:
                    LosslessTransformApply.ApplyColorTransform(
                        buffer, currentWidth, transform.SizeBits, transform.Data);
                    break;

                case TransformKind.SubtractGreen:
                    LosslessTransformApply.ApplySubtractGreenTransform(buffer);
                    break;

                case TransformKind.ColorIndexing:
                    currentWidth = _width;
                    imageSize = currentWidth * _height * 4;
                    LosslessTransformApply.ApplyColorIndexingTransform(
                        buffer, currentWidth, _height, transform.TableSize, transform.Data);
                    break;
            }
        }
    }

    private ushort ReadTransforms()
    {
        ushort xsize = _width;

        while (_bitReader.ReadBit())
        {
            byte transformType = (byte)_bitReader.ReadBits(2);

            if (_transforms[transformType] != null)
                throw new WebPDecodingException("Duplicate transform type");

            _transformOrder.Add(transformType);

            switch (transformType)
            {
                case 0: // Predictor
                {
                    byte sizeBits = (byte)(_bitReader.ReadBits(3) + 2);
                    ushort blockXSize = LosslessTransformApply.SubsampleSize(xsize, sizeBits);
                    ushort blockYSize = LosslessTransformApply.SubsampleSize(_height, sizeBits);

                    byte[] predictorData = new byte[blockXSize * blockYSize * 4];
                    DecodeImageStream(blockXSize, blockYSize, false, predictorData, 0, predictorData.Length);

                    _transforms[transformType] = new LosslessTransform
                    {
                        Kind = TransformKind.Predictor,
                        SizeBits = sizeBits,
                        Data = predictorData
                    };
                    break;
                }

                case 1: // Color
                {
                    byte sizeBits = (byte)(_bitReader.ReadBits(3) + 2);
                    ushort blockXSize = LosslessTransformApply.SubsampleSize(xsize, sizeBits);
                    ushort blockYSize = LosslessTransformApply.SubsampleSize(_height, sizeBits);

                    byte[] transformData = new byte[blockXSize * blockYSize * 4];
                    DecodeImageStream(blockXSize, blockYSize, false, transformData, 0, transformData.Length);

                    _transforms[transformType] = new LosslessTransform
                    {
                        Kind = TransformKind.Color,
                        SizeBits = sizeBits,
                        Data = transformData
                    };
                    break;
                }

                case 2: // Subtract Green
                    _transforms[transformType] = new LosslessTransform
                    {
                        Kind = TransformKind.SubtractGreen
                    };
                    break;

                case 3: // Color Indexing
                {
                    ushort colorTableSize = (ushort)(_bitReader.ReadBits(8) + 1);
                    byte[] colorMap = new byte[colorTableSize * 4];
                    DecodeImageStream(colorTableSize, 1, false, colorMap, 0, colorMap.Length);

                    byte bits;
                    if (colorTableSize <= 2)
                        bits = 3;
                    else if (colorTableSize <= 4)
                        bits = 2;
                    else if (colorTableSize <= 16)
                        bits = 1;
                    else
                        bits = 0;

                    xsize = LosslessTransformApply.SubsampleSize(xsize, bits);
                    LosslessTransformApply.AdjustColorMap(colorMap);

                    _transforms[transformType] = new LosslessTransform
                    {
                        Kind = TransformKind.ColorIndexing,
                        TableSize = colorTableSize,
                        Data = colorMap
                    };
                    break;
                }
            }
        }

        return xsize;
    }

    private void DecodeImageStream(ushort xsize, ushort ysize, bool isArgbImg, byte[] data, int offset, int length)
    {
        byte? colorCacheBits = ReadColorCache();
        ColorCache colorCache = colorCacheBits.HasValue
            ? new ColorCache(colorCacheBits.Value)
            : null;

        var huffmanInfo = ReadHuffmanCodes(isArgbImg, xsize, ysize, colorCache);
        DecodeImageData(xsize, ysize, huffmanInfo, data, offset, length);
    }

    private byte? ReadColorCache()
    {
        if (_bitReader.ReadBit())
        {
            byte codeBits = (byte)_bitReader.ReadBits(4);
            if (codeBits < 1 || codeBits > 11)
                throw new WebPDecodingException($"Invalid color cache bits: {codeBits}");
            return codeBits;
        }
        return null;
    }

    private HuffmanInfo ReadHuffmanCodes(bool readMeta, ushort xsize, ushort ysize, ColorCache colorCache)
    {
        uint numHuffGroups = 1;
        byte huffmanBits = 0;
        ushort huffmanXSize = 1;
        ushort[] entropyImage = Array.Empty<ushort>();

        if (readMeta && _bitReader.ReadBit())
        {
            huffmanBits = (byte)(_bitReader.ReadBits(3) + 2);
            huffmanXSize = LosslessTransformApply.SubsampleSize(xsize, huffmanBits);
            ushort huffmanYSize = LosslessTransformApply.SubsampleSize(ysize, huffmanBits);

            byte[] data = new byte[huffmanXSize * huffmanYSize * 4];
            DecodeImageStream(huffmanXSize, huffmanYSize, false, data, 0, data.Length);

            entropyImage = new ushort[huffmanXSize * huffmanYSize];
            for (int i = 0; i < entropyImage.Length; i++)
            {
                ushort metaHuffCode = (ushort)((data[i * 4] << 8) | data[i * 4 + 1]);
                if (metaHuffCode >= numHuffGroups)
                    numHuffGroups = (uint)(metaHuffCode + 1);
                entropyImage[i] = metaHuffCode;
            }
        }

        var huffmanCodeGroups = new List<HuffmanCodeGroup>();
        for (int i = 0; i < numHuffGroups; i++)
        {
            var group = new HuffmanCodeGroup();
            for (int j = 0; j < HuffmanCodesPerMetaCode; j++)
            {
                ushort alphabetSizeForCode = AlphabetSize[j];
                if (j == 0 && colorCache != null)
                    alphabetSizeForCode += (ushort)(1 << colorCache.Bits);

                group[j] = ReadHuffmanCode(alphabetSizeForCode);
            }
            huffmanCodeGroups.Add(group);
        }

        ushort huffmanMask = huffmanBits == 0 ? (ushort)0xFFFF : (ushort)((1 << huffmanBits) - 1);

        return new HuffmanInfo
        {
            XSize = huffmanXSize,
            ColorCache = colorCache,
            Image = entropyImage,
            Bits = huffmanBits,
            Mask = huffmanMask,
            HuffmanCodeGroups = huffmanCodeGroups
        };
    }

    private HuffmanTree ReadHuffmanCode(ushort alphabetSize)
    {
        bool simple = _bitReader.ReadBit();

        if (simple)
        {
            byte numSymbols = (byte)(_bitReader.ReadBits(1) + 1);
            byte isFirst8Bits = (byte)_bitReader.ReadBits(1);
            ushort zeroSymbol = (ushort)_bitReader.ReadBits(1 + 7 * isFirst8Bits);

            if (zeroSymbol >= alphabetSize)
                throw new WebPDecodingException("Invalid huffman symbol");

            if (numSymbols == 1)
                return HuffmanTree.BuildSingleNode(zeroSymbol);

            ushort oneSymbol = (ushort)_bitReader.ReadBits(8);
            if (oneSymbol >= alphabetSize)
                throw new WebPDecodingException("Invalid huffman symbol");

            return HuffmanTree.BuildTwoNode(zeroSymbol, oneSymbol);
        }

        ushort[] codeLengthCodeLengths = new ushort[19];
        int numCodeLengths = 4 + (int)_bitReader.ReadBits(4);
        for (int i = 0; i < numCodeLengths; i++)
            codeLengthCodeLengths[CodeLengthCodeOrder[i]] = (ushort)_bitReader.ReadBits(3);

        ushort[] newCodeLengths = ReadHuffmanCodeLengths(codeLengthCodeLengths, alphabetSize);
        return HuffmanTree.BuildImplicit(newCodeLengths);
    }

    private ushort[] ReadHuffmanCodeLengths(ushort[] codeLengthCodeLengths, ushort numSymbols)
    {
        var table = HuffmanTree.BuildImplicit(codeLengthCodeLengths);

        ushort maxSymbol;
        if (_bitReader.ReadBit())
        {
            byte lengthNBits = (byte)(2 + 2 * _bitReader.ReadBits(3));
            ushort maxMinusTwo = (ushort)_bitReader.ReadBits(lengthNBits);
            if (maxMinusTwo > numSymbols - 2)
                throw new WebPDecodingException("Invalid huffman code length");
            maxSymbol = (ushort)(2 + maxMinusTwo);
        }
        else
        {
            maxSymbol = numSymbols;
        }

        ushort[] codeLengths = new ushort[numSymbols];
        ushort prevCodeLen = 8;
        ushort symbol = 0;

        while (symbol < numSymbols)
        {
            if (maxSymbol == 0)
                break;
            maxSymbol--;

            _bitReader.Fill();
            ushort codeLen = table.ReadSymbol(_bitReader);

            if (codeLen < 16)
            {
                codeLengths[symbol++] = codeLen;
                if (codeLen != 0)
                    prevCodeLen = codeLen;
            }
            else
            {
                bool usePrev = codeLen == 16;
                int slot = codeLen - 16;
                int extraBits = slot switch { 0 => 2, 1 => 3, 2 => 7, _ => throw new WebPDecodingException("Invalid slot") };
                int repeatOffset = slot switch { 0 or 1 => 3, 2 => 11, _ => throw new WebPDecodingException("Invalid slot") };

                int repeat = (int)_bitReader.ReadBits(extraBits) + repeatOffset;
                if (symbol + repeat > numSymbols)
                    throw new WebPDecodingException("Invalid repeat count");

                ushort length = usePrev ? prevCodeLen : (ushort)0;
                while (repeat-- > 0)
                    codeLengths[symbol++] = length;
            }
        }

        return codeLengths;
    }

    private void DecodeImageData(ushort width, ushort height, HuffmanInfo huffmanInfo, byte[] data, int offset, int length)
    {
        int numValues = width * height;
        int huffIndex = huffmanInfo.GetHuffIndex(0, 0);
        HuffmanCodeGroup tree = huffmanInfo.HuffmanCodeGroups[huffIndex];
        int index = 0;
        int nextBlockStart = 0;

        while (index < numValues)
        {
            _bitReader.Fill();

            if (index >= nextBlockStart)
            {
                int x = index % width;
                int y = index / width;
                nextBlockStart = Math.Min(x | huffmanInfo.Mask, width - 1) + y * width + 1;

                huffIndex = huffmanInfo.GetHuffIndex((ushort)x, (ushort)y);
                tree = huffmanInfo.HuffmanCodeGroups[huffIndex];

                // Fast path: all single-symbol trees
                if (tree[0].IsSingleNode && tree[1].IsSingleNode &&
                    tree[2].IsSingleNode && tree[3].IsSingleNode)
                {
                    ushort code = tree[Green].ReadSymbol(_bitReader);
                    if (code < 256)
                    {
                        int n = huffmanInfo.Bits == 0 ? numValues : nextBlockStart - index;
                        byte red = (byte)tree[Red].ReadSymbol(_bitReader);
                        byte blue = (byte)tree[Blue].ReadSymbol(_bitReader);
                        byte alpha = (byte)tree[Alpha].ReadSymbol(_bitReader);
                        byte green = (byte)code;

                        for (int i = 0; i < n; i++)
                        {
                            int pixelOffset = offset + (index + i) * 4;
                            data[pixelOffset] = red;
                            data[pixelOffset + 1] = green;
                            data[pixelOffset + 2] = blue;
                            data[pixelOffset + 3] = alpha;
                        }

                        huffmanInfo.ColorCache?.Insert(red, green, blue, alpha);
                        index += n;
                        continue;
                    }
                }
            }

            ushort mainCode = tree[Green].ReadSymbol(_bitReader);

            if (mainCode < 256)
            {
                // Literal
                byte green = (byte)mainCode;
                byte red = (byte)tree[Red].ReadSymbol(_bitReader);
                byte blue = (byte)tree[Blue].ReadSymbol(_bitReader);

                if (_bitReader.BitsAvailable < 15)
                    _bitReader.Fill();

                byte alpha = (byte)tree[Alpha].ReadSymbol(_bitReader);

                int pixelOffset = offset + index * 4;
                data[pixelOffset] = red;
                data[pixelOffset + 1] = green;
                data[pixelOffset + 2] = blue;
                data[pixelOffset + 3] = alpha;

                huffmanInfo.ColorCache?.Insert(red, green, blue, alpha);
                index++;
            }
            else if (mainCode < 256 + 24)
            {
                // Backward reference
                ushort lengthSymbol = (ushort)(mainCode - 256);
                int copyLength = GetCopyDistance(lengthSymbol);

                ushort distSymbol = tree[Dist].ReadSymbol(_bitReader);
                int distCode = GetCopyDistance(distSymbol);
                int dist = PlaneCodeToDistance(width, distCode);

                if (index < dist || numValues - index < copyLength)
                    throw new WebPDecodingException("Invalid backward reference");

                if (dist == 1)
                {
                    // Repeat single pixel
                    int srcOffset = offset + (index - dist) * 4;
                    byte r = data[srcOffset];
                    byte g = data[srcOffset + 1];
                    byte b = data[srcOffset + 2];
                    byte a = data[srcOffset + 3];

                    for (int i = 0; i < copyLength; i++)
                    {
                        int destOffset = offset + (index + i) * 4;
                        data[destOffset] = r;
                        data[destOffset + 1] = g;
                        data[destOffset + 2] = b;
                        data[destOffset + 3] = a;
                    }
                }
                else
                {
                    // Copy pixels
                    int srcStart = offset + (index - dist) * 4;
                    int destStart = offset + index * 4;
                    for (int i = 0; i < copyLength * 4; i++)
                        data[destStart + i] = data[srcStart + i];

                    // Update color cache
                    if (huffmanInfo.ColorCache != null)
                    {
                        for (int i = 0; i < copyLength; i++)
                        {
                            int pixelOffset = destStart + i * 4;
                            huffmanInfo.ColorCache.Insert(
                                data[pixelOffset],
                                data[pixelOffset + 1],
                                data[pixelOffset + 2],
                                data[pixelOffset + 3]);
                        }
                    }
                }
                index += copyLength;
            }
            else
            {
                // Color cache lookup
                if (huffmanInfo.ColorCache == null)
                    throw new WebPDecodingException("Color cache used but not present");

                var color = huffmanInfo.ColorCache.Lookup(mainCode - 280);
                int pixelOffset = offset + index * 4;
                data[pixelOffset] = color.r;
                data[pixelOffset + 1] = color.g;
                data[pixelOffset + 2] = color.b;
                data[pixelOffset + 3] = color.a;
                index++;
            }
        }
    }

    private int GetCopyDistance(ushort prefixCode)
    {
        if (prefixCode < 4)
            return prefixCode + 1;

        int extraBits = (prefixCode - 2) >> 1;
        int baseValue = (2 + (prefixCode & 1)) << extraBits;
        int extra = (int)_bitReader.ReadBits(extraBits);
        return baseValue + extra + 1;
    }

    private static int PlaneCodeToDistance(ushort xsize, int planeCode)
    {
        if (planeCode > 120)
            return planeCode - 120;

        var (dx, dy) = WebPConstants.DistanceMap[planeCode - 1];
        int dist = dx + dy * xsize;
        return dist < 1 ? 1 : dist;
    }
}

/// <summary>
/// Huffman info for image decoding.
/// </summary>
internal class HuffmanInfo
{
    public ushort XSize { get; set; }
    public ColorCache ColorCache { get; set; }
    public ushort[] Image { get; set; }
    public byte Bits { get; set; }
    public ushort Mask { get; set; }
    public List<HuffmanCodeGroup> HuffmanCodeGroups { get; set; }

    public int GetHuffIndex(ushort x, ushort y)
    {
        if (Bits == 0)
            return 0;
        int position = (y >> Bits) * XSize + (x >> Bits);
        return Image[position];
    }
}

/// <summary>
/// Color cache for VP8L decoding.
/// </summary>
internal class ColorCache
{
    private const uint HashMul = 0x1e35a7bd;
    private readonly byte _bits;
    private readonly (byte r, byte g, byte b, byte a)[] _cache;

    public ColorCache(byte bits)
    {
        _bits = bits;
        _cache = new (byte, byte, byte, byte)[1 << bits];
    }

    public byte Bits => _bits;

    public void Insert(byte r, byte g, byte b, byte a)
    {
        uint color = ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
        uint index = (HashMul * color) >> (32 - _bits);
        _cache[index] = (r, g, b, a);
    }

    public (byte r, byte g, byte b, byte a) Lookup(int index)
    {
        return _cache[index];
    }
}
