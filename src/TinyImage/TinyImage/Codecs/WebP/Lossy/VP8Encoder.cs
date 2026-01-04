using System;
using System.Collections.Generic;
using System.IO;
using TinyImage.Codecs.WebP.Core;

// Use the Plane enum from WebPConstants
using Plane = TinyImage.Codecs.WebP.Core.Plane;

namespace TinyImage.Codecs.WebP.Lossy;

/// <summary>
/// VP8 lossy WebP encoder.
/// Translated from webp-rust vp8_encoder.rs
/// </summary>
internal class VP8Encoder
{
    private readonly Stream _writer;
    private ArithmeticEncoder _encoder;
    private readonly List<ArithmeticEncoder> _partitions;
    
    private int _width;
    private int _height;
    private int _macroBlockWidth;
    private int _macroBlockHeight;
    
    private byte[] _yBuf;
    private byte[] _uBuf;
    private byte[] _vBuf;
    
    private byte _quantIndex;
    private Segment _segment;
    private byte _skipProb;  // Probability for skip coefficient flag
    private int _lambda;     // Rate-distortion lambda for mode selection
    
    // Complexity tracking for coefficient encoding
    private Complexity[] _topComplexity;
    private Complexity _leftComplexity;
    
    // Prediction mode tracking
    private IntraMode[] _topBPred;
    private IntraMode[] _leftBPred;
    
    // Borders for prediction
    private byte[] _leftBorderY;
    private byte[] _leftBorderU;
    private byte[] _leftBorderV;
    private byte[] _topBorderY;
    private byte[] _topBorderU;
    private byte[] _topBorderV;

    public VP8Encoder(Stream stream)
    {
        _writer = stream ?? throw new ArgumentNullException(nameof(stream));
        _encoder = new ArithmeticEncoder();
        _partitions = new List<ArithmeticEncoder> { new ArithmeticEncoder() };
    }

    /// <summary>
    /// Encodes RGBA pixel data to VP8 lossy format.
    /// </summary>
    public void Encode(byte[] rgba, int width, int height, int quality = 75)
    {
        if (width <= 0 || width > 16384 || height <= 0 || height > 16384)
            throw new WebPEncodingException("Invalid image dimensions");

        if (quality < 0 || quality > 100)
            throw new WebPEncodingException("Quality must be between 0 and 100");

        _width = width;
        _height = height;
        _macroBlockWidth = (width + 15) / 16;
        _macroBlockHeight = (height + 15) / 16;

        // Convert RGB(A) to YUV
        ConvertToYuv(rgba, width, height);

        // Set up quantization based on quality
        SetupQuantization(quality);

        // Initialize borders and complexity
        InitializeBorders();

        // Encode the frame
        EncodeFrame();
    }

    private void ConvertToYuv(byte[] rgba, int width, int height)
    {
        int mbWidth = _macroBlockWidth * 16;
        int mbHeight = _macroBlockHeight * 16;
        
        _yBuf = new byte[mbWidth * mbHeight];
        _uBuf = new byte[(mbWidth / 2) * (mbHeight / 2)];
        _vBuf = new byte[(mbWidth / 2) * (mbHeight / 2)];

        int chromaWidth = mbWidth / 2;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int srcIdx = (y * width + x) * 4;
                byte r = rgba[srcIdx];
                byte g = rgba[srcIdx + 1];
                byte b = rgba[srcIdx + 2];

                // YUV conversion (BT.601)
                int yy = ((66 * r + 129 * g + 25 * b + 128) >> 8) + 16;
                _yBuf[y * mbWidth + x] = (byte)MathExt.Clamp(yy, 0, 255);

                // Subsample chroma (2x2 block average)
                if ((x & 1) == 0 && (y & 1) == 0)
                {
                    int uu = ((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128;
                    int vv = ((112 * r - 94 * g - 18 * b + 128) >> 8) + 128;
                    
                    int chromaIdx = (y / 2) * chromaWidth + (x / 2);
                    _uBuf[chromaIdx] = (byte)MathExt.Clamp(uu, 0, 255);
                    _vBuf[chromaIdx] = (byte)MathExt.Clamp(vv, 0, 255);
                }
            }
        }

        // Pad remaining pixels
        for (int y = height; y < mbHeight; y++)
        {
            for (int x = 0; x < mbWidth; x++)
            {
                _yBuf[y * mbWidth + x] = _yBuf[(height - 1) * mbWidth + Math.Min(x, width - 1)];
            }
        }
        
        for (int y = 0; y < mbHeight; y++)
        {
            for (int x = width; x < mbWidth; x++)
            {
                _yBuf[y * mbWidth + x] = _yBuf[y * mbWidth + width - 1];
            }
        }

        // Pad chroma
        int chromaH = mbHeight / 2;
        int srcChromaH = (height + 1) / 2;
        int srcChromaW = (width + 1) / 2;
        
        for (int y = srcChromaH; y < chromaH; y++)
        {
            for (int x = 0; x < chromaWidth; x++)
            {
                _uBuf[y * chromaWidth + x] = _uBuf[(srcChromaH - 1) * chromaWidth + Math.Min(x, srcChromaW - 1)];
                _vBuf[y * chromaWidth + x] = _vBuf[(srcChromaH - 1) * chromaWidth + Math.Min(x, srcChromaW - 1)];
            }
        }
        
        for (int y = 0; y < chromaH; y++)
        {
            for (int x = srcChromaW; x < chromaWidth; x++)
            {
                _uBuf[y * chromaWidth + x] = _uBuf[y * chromaWidth + srcChromaW - 1];
                _vBuf[y * chromaWidth + x] = _vBuf[y * chromaWidth + srcChromaW - 1];
            }
        }
    }

    private void SetupQuantization(int quality)
    {
        // Map quality 0-100 to quant index 127-0
        _quantIndex = (byte)(127 - quality * 127 / 100);
        
        // Skip probability - lower quality = more zeros = higher skip probability
        _skipProb = (byte)Math.Min(250, 128 + _quantIndex);
        
        // Lambda for rate-distortion optimization (higher = favor smaller files)
        // Based on libwebp's lambda calculation
        _lambda = (_quantIndex < 37) ? 1 : (_quantIndex < 75) ? 2 : (_quantIndex < 112) ? 3 : 4;
        
        int qi = _quantIndex;
        _segment = new Segment
        {
            YDc = WebPConstants.DcQuant[qi],
            YAc = WebPConstants.AcQuant[qi],
            Y2Dc = (short)(WebPConstants.DcQuant[qi] * 2),
            Y2Ac = (short)Math.Max(8, WebPConstants.AcQuant[qi] * 155 / 100),
            UvDc = WebPConstants.DcQuant[qi],
            UvAc = WebPConstants.AcQuant[qi]
        };
    }

    private void InitializeBorders()
    {
        _topComplexity = new Complexity[_macroBlockWidth];
        for (int i = 0; i < _macroBlockWidth; i++)
            _topComplexity[i] = new Complexity();
        _leftComplexity = new Complexity();

        _topBPred = new IntraMode[_macroBlockWidth * 4];
        _leftBPred = new IntraMode[4];

        _leftBorderY = new byte[17];
        _leftBorderU = new byte[9];
        _leftBorderV = new byte[9];

        _topBorderY = new byte[_macroBlockWidth * 16 + 4];
        _topBorderU = new byte[_macroBlockWidth * 8];
        _topBorderV = new byte[_macroBlockWidth * 8];

        // Initialize with gray values
        for (int i = 0; i < _leftBorderY.Length; i++) _leftBorderY[i] = 129;
        for (int i = 0; i < _leftBorderU.Length; i++) _leftBorderU[i] = 129;
        for (int i = 0; i < _leftBorderV.Length; i++) _leftBorderV[i] = 129;
        for (int i = 0; i < _topBorderY.Length; i++) _topBorderY[i] = 127;
        for (int i = 0; i < _topBorderU.Length; i++) _topBorderU[i] = 127;
        for (int i = 0; i < _topBorderV.Length; i++) _topBorderV[i] = 127;
    }

    private void EncodeFrame()
    {
        // Encode compressed header
        EncodeCompressedHeader();

        // Encode macroblocks
        for (int mby = 0; mby < _macroBlockHeight; mby++)
        {
            int partitionIndex = mby % _partitions.Count;
            
            // Reset left complexity for row
            _leftComplexity = new Complexity();
            for (int i = 0; i < 4; i++) _leftBPred[i] = IntraMode.DC;
            for (int i = 0; i < _leftBorderY.Length; i++) _leftBorderY[i] = 129;
            for (int i = 0; i < _leftBorderU.Length; i++) _leftBorderU[i] = 129;
            for (int i = 0; i < _leftBorderV.Length; i++) _leftBorderV[i] = 129;

            for (int mbx = 0; mbx < _macroBlockWidth; mbx++)
            {
                // Select best luma prediction mode using RD optimization
                var (bestLumaMode, yBlocks) = SelectBestLumaMode(mbx, mby);
                
                // Select best chroma prediction mode using RD optimization  
                var (bestChromaMode, uBlocks, vBlocks) = SelectBestChromaMode(mbx, mby);
                
                var info = new MacroBlockInfo
                {
                    LumaMode = bestLumaMode,
                    ChromaMode = bestChromaMode,
                    CoeffsSkipped = false
                };
                
                // Check if all coefficients are zero after quantization
                info.CoeffsSkipped = AreAllCoefficientsZero(yBlocks, uBlocks, vBlocks, info.LumaMode);

                // Write macroblock header (includes skip flag)
                WriteMacroBlockHeader(info, mbx);

                if (!info.CoeffsSkipped)
                {
                    // Encode residual data
                    EncodeResidualData(info, partitionIndex, mbx, yBlocks, uBlocks, vBlocks);
                }
                else
                {
                    // Clear complexities for skipped macroblocks
                    _leftComplexity.Clear(info.LumaMode != LumaMode.B);
                    _topComplexity[mbx].Clear(info.LumaMode != LumaMode.B);
                }
            }
        }

        // Flush encoder and get compressed header
        byte[] compressedHeader = _encoder.FlushAndGetBuffer();

        // Write uncompressed frame header
        WriteUncompressedHeader((uint)compressedHeader.Length);

        // Write compressed header
        _writer.Write(compressedHeader, 0, compressedHeader.Length);

        // Write partitions
        WritePartitions();
    }

    private void EncodeCompressedHeader()
    {
        // Color space (must be 0 for keyframe)
        _encoder.WriteLiteral(1, 0);
        // Pixel type
        _encoder.WriteLiteral(1, 0);

        // Segments disabled
        _encoder.WriteFlag(false);

        // Filter type (simple)
        _encoder.WriteFlag(false);
        // Filter level
        _encoder.WriteLiteral(6, 63);
        // Sharpness level
        _encoder.WriteLiteral(3, 7);

        // Loop filter adjustments disabled
        _encoder.WriteFlag(false);

        // Number of partitions (0 = 1 partition)
        _encoder.WriteLiteral(2, 0);

        // Quantization indices
        _encoder.WriteLiteral(7, _quantIndex);
        _encoder.WriteOptionalSignedValue(4, null); // ydc delta
        _encoder.WriteOptionalSignedValue(4, null); // y2dc delta
        _encoder.WriteOptionalSignedValue(4, null); // y2ac delta
        _encoder.WriteOptionalSignedValue(4, null); // uvdc delta
        _encoder.WriteOptionalSignedValue(4, null); // uvac delta

        // Refresh entropy probs
        _encoder.WriteLiteral(1, 0);

        // Token probability updates
        EncodeTokenProbabilities();

        // Enable skip coefficient optimization
        _encoder.WriteLiteral(1, 1);  // mb_no_skip_coeff = true
        _encoder.WriteLiteral(8, _skipProb);  // prob_skip_false
    }

    private void EncodeTokenProbabilities()
    {
        // Don't update any probabilities - use defaults
        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < 8; j++)
            {
                for (int k = 0; k < 3; k++)
                {
                    for (int l = 0; l < 11; l++)
                    {
                        _encoder.WriteBool(false, WebPConstants.CoeffUpdateProbs[i][j][k][l]);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Checks if all quantized coefficients are zero, allowing the macroblock to be skipped.
    /// </summary>
    private bool AreAllCoefficientsZero(int[] yBlocks, int[] uBlocks, int[] vBlocks, LumaMode lumaMode)
    {
        int firstCoeff = lumaMode == LumaMode.B ? 0 : 1;
        
        // Check Y blocks (skip DC if not B mode, as it's in Y2)
        for (int block = 0; block < 16; block++)
        {
            for (int i = firstCoeff; i < 16; i++)
            {
                int zigzag = WebPConstants.Zigzag[i];
                int coeff = yBlocks[block * 16 + zigzag];
                if (coeff / _segment.YAc != 0)
                    return false;
            }
        }
        
        // Check Y2 (DC coefficients) if not B mode
        if (lumaMode != LumaMode.B)
        {
            var y2Coeffs = new int[16];
            for (int i = 0; i < 16; i++)
                y2Coeffs[i] = yBlocks[i * 16];
            WhtTransform.Wht4x4(y2Coeffs);
            
            for (int i = 0; i < 16; i++)
            {
                short quant = i > 0 ? _segment.Y2Ac : _segment.Y2Dc;
                if (y2Coeffs[i] / quant != 0)
                    return false;
            }
        }
        
        // Check U and V blocks
        for (int block = 0; block < 4; block++)
        {
            for (int i = 0; i < 16; i++)
            {
                int zigzag = WebPConstants.Zigzag[i];
                short quant = zigzag > 0 ? _segment.UvAc : _segment.UvDc;
                if (uBlocks[block * 16 + zigzag] / quant != 0)
                    return false;
                if (vBlocks[block * 16 + zigzag] / quant != 0)
                    return false;
            }
        }
        
        return true;
    }

    /// <summary>
    /// Selects the best luma prediction mode (DC, V, H, TM) using rate-distortion optimization.
    /// Returns the best mode and the transformed coefficients.
    /// </summary>
    private (LumaMode mode, int[] yBlocks) SelectBestLumaMode(int mbx, int mby)
    {
        const int stride = 21; // 1 + 16 + 4
        int mbWidth = _macroBlockWidth * 16;
        
        // Create border once for all modes
        var yWithBorder = new byte[stride * stride];
        CreateLumaBorder(yWithBorder, mbx, mby, stride);
        
        LumaMode bestMode = LumaMode.DC;
        int[] bestBlocks = null;
        long bestScore = long.MaxValue;
        
        // Available modes depend on position (V needs top, H needs left)
        var modesToTry = new List<LumaMode> { LumaMode.DC };
        if (mby > 0) modesToTry.Add(LumaMode.V);
        if (mbx > 0) modesToTry.Add(LumaMode.H);
        if (mby > 0 && mbx > 0) modesToTry.Add(LumaMode.TM);
        
        foreach (var mode in modesToTry)
        {
            // Make a copy of the border for this mode
            var testBorder = new byte[stride * stride];
            Array.Copy(yWithBorder, testBorder, yWithBorder.Length);
            
            // Apply prediction
            ApplyLumaPrediction(testBorder, 16, stride, mode, mby > 0, mbx > 0);
            
            // Compute residuals and transform
            var blocks = new int[16 * 16];
            long distortion = 0;
            
            for (int blockY = 0; blockY < 4; blockY++)
            {
                for (int blockX = 0; blockX < 4; blockX++)
                {
                    int blockIndex = blockY * 4 * 16 + blockX * 16;
                    int borderIndex = (blockY * 4 + 1) * stride + blockX * 4 + 1;
                    int dataIndex = (mby * 16 + blockY * 4) * mbWidth + mbx * 16 + blockX * 4;
                    
                    var block = new int[16];
                    for (int y = 0; y < 4; y++)
                    {
                        for (int x = 0; x < 4; x++)
                        {
                            byte predicted = testBorder[borderIndex + y * stride + x];
                            byte actual = _yBuf[dataIndex + y * mbWidth + x];
                            int residual = actual - predicted;
                            block[y * 4 + x] = residual;
                            distortion += residual * residual;  // SSE
                        }
                    }
                    
                    // Apply DCT
                    DctTransform.Dct4x4(block);
                    Array.Copy(block, 0, blocks, blockIndex, 16);
                }
            }
            
            // Estimate rate (simplified - count non-zero quantized coefficients)
            int nonZeroCoeffs = 0;
            for (int i = 0; i < blocks.Length; i++)
            {
                int zigzag = WebPConstants.Zigzag[i % 16];
                int quant = zigzag > 0 ? _segment.YAc : _segment.YDc;
                if (blocks[i] / quant != 0)
                    nonZeroCoeffs++;
            }
            
            // RD score = distortion + lambda * rate (simplified)
            // Add mode cost: DC=0, V/H=~2 bits, TM=~4 bits
            int modeCost = mode switch
            {
                LumaMode.DC => 10,
                LumaMode.V => 20,
                LumaMode.H => 20,
                LumaMode.TM => 40,
                _ => 10
            };
            
            long score = distortion + _lambda * (nonZeroCoeffs * 50 + modeCost);
            
            if (score < bestScore)
            {
                bestScore = score;
                bestMode = mode;
                bestBlocks = blocks;
            }
        }
        
        // Now do the full transform with border updates for the selected mode
        var info = new MacroBlockInfo { LumaMode = bestMode };
        var finalBlocks = TransformLumaBlock(mbx, mby, info);
        
        return (bestMode, finalBlocks);
    }

    /// <summary>
    /// Selects the best chroma prediction mode (DC, V, H, TM) using rate-distortion optimization.
    /// </summary>
    private (ChromaMode mode, int[] uBlocks, int[] vBlocks) SelectBestChromaMode(int mbx, int mby)
    {
        const int stride = 9; // 1 + 8
        int chromaWidth = _macroBlockWidth * 8;
        
        ChromaMode bestMode = ChromaMode.DC;
        long bestScore = long.MaxValue;
        
        // Available modes depend on position
        var modesToTry = new List<ChromaMode> { ChromaMode.DC };
        if (mby > 0) modesToTry.Add(ChromaMode.V);
        if (mbx > 0) modesToTry.Add(ChromaMode.H);
        if (mby > 0 && mbx > 0) modesToTry.Add(ChromaMode.TM);
        
        foreach (var mode in modesToTry)
        {
            // Create borders for U and V
            var uWithBorder = new byte[stride * stride];
            var vWithBorder = new byte[stride * stride];
            CreateChromaBorder(uWithBorder, mbx, mby, _topBorderU, _leftBorderU);
            CreateChromaBorder(vWithBorder, mbx, mby, _topBorderV, _leftBorderV);
            
            // Apply prediction
            ApplyChromaPrediction(uWithBorder, 8, stride, mode, mby > 0, mbx > 0);
            ApplyChromaPrediction(vWithBorder, 8, stride, mode, mby > 0, mbx > 0);
            
            // Compute distortion
            long distortion = 0;
            int nonZeroCoeffs = 0;
            
            for (int blockY = 0; blockY < 2; blockY++)
            {
                for (int blockX = 0; blockX < 2; blockX++)
                {
                    int borderIndex = (blockY * 4 + 1) * stride + blockX * 4 + 1;
                    int dataIndex = (mby * 8 + blockY * 4) * chromaWidth + mbx * 8 + blockX * 4;
                    
                    for (int y = 0; y < 4; y++)
                    {
                        for (int x = 0; x < 4; x++)
                        {
                            int uRes = _uBuf[dataIndex + y * chromaWidth + x] - uWithBorder[borderIndex + y * stride + x];
                            int vRes = _vBuf[dataIndex + y * chromaWidth + x] - vWithBorder[borderIndex + y * stride + x];
                            distortion += uRes * uRes + vRes * vRes;
                            
                            // Estimate non-zero coefficients
                            if (Math.Abs(uRes) > _segment.UvAc / 2) nonZeroCoeffs++;
                            if (Math.Abs(vRes) > _segment.UvAc / 2) nonZeroCoeffs++;
                        }
                    }
                }
            }
            
            // Mode cost
            int modeCost = mode switch
            {
                ChromaMode.DC => 10,
                ChromaMode.V => 20,
                ChromaMode.H => 20,
                ChromaMode.TM => 40,
                _ => 10
            };
            
            long score = distortion + _lambda * (nonZeroCoeffs * 30 + modeCost);
            
            if (score < bestScore)
            {
                bestScore = score;
                bestMode = mode;
            }
        }
        
        // Do the full transform with the selected mode
        var (uBlocks, vBlocks) = TransformChromaBlocks(mbx, mby, bestMode);
        return (bestMode, uBlocks, vBlocks);
    }

    /// <summary>
    /// Applies luma prediction to the bordered buffer.
    /// </summary>
    private static void ApplyLumaPrediction(byte[] buffer, int size, int stride, LumaMode mode, bool hasTop, bool hasLeft)
    {
        switch (mode)
        {
            case LumaMode.DC:
                PredictDCStatic(buffer, size, stride, hasTop, hasLeft);
                break;
            case LumaMode.V:
                PredictVPred(buffer, size, stride);
                break;
            case LumaMode.H:
                PredictHPred(buffer, size, stride);
                break;
            case LumaMode.TM:
                PredictTMPred(buffer, size, stride);
                break;
        }
    }

    /// <summary>
    /// Applies chroma prediction to the bordered buffer.
    /// </summary>
    private static void ApplyChromaPrediction(byte[] buffer, int size, int stride, ChromaMode mode, bool hasTop, bool hasLeft)
    {
        switch (mode)
        {
            case ChromaMode.DC:
                PredictDCStatic(buffer, size, stride, hasTop, hasLeft);
                break;
            case ChromaMode.V:
                PredictVPred(buffer, size, stride);
                break;
            case ChromaMode.H:
                PredictHPred(buffer, size, stride);
                break;
            case ChromaMode.TM:
                PredictTMPred(buffer, size, stride);
                break;
        }
    }

    /// <summary>
    /// DC prediction - average of top and left pixels.
    /// </summary>
    private static void PredictDCStatic(byte[] buffer, int size, int stride, bool hasTop, bool hasLeft)
    {
        int sum = 0;
        int count = 0;

        if (hasTop)
        {
            for (int x = 0; x < size; x++)
                sum += buffer[x + 1];
            count += size;
        }

        if (hasLeft)
        {
            for (int y = 0; y < size; y++)
                sum += buffer[(y + 1) * stride];
            count += size;
        }

        byte dc = count > 0 ? (byte)((sum + count / 2) / count) : (byte)128;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                buffer[(y + 1) * stride + x + 1] = dc;
            }
        }
    }

    /// <summary>
    /// Vertical prediction - copy from top row.
    /// </summary>
    private static void PredictVPred(byte[] buffer, int size, int stride)
    {
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                buffer[(y + 1) * stride + x + 1] = buffer[x + 1];  // Top row
            }
        }
    }

    /// <summary>
    /// Horizontal prediction - copy from left column.
    /// </summary>
    private static void PredictHPred(byte[] buffer, int size, int stride)
    {
        for (int y = 0; y < size; y++)
        {
            byte leftVal = buffer[(y + 1) * stride];  // Left column
            for (int x = 0; x < size; x++)
            {
                buffer[(y + 1) * stride + x + 1] = leftVal;
            }
        }
    }

    /// <summary>
    /// TrueMotion prediction - top + left - top_left.
    /// </summary>
    private static void PredictTMPred(byte[] buffer, int size, int stride)
    {
        byte topLeft = buffer[0];
        for (int y = 0; y < size; y++)
        {
            byte leftVal = buffer[(y + 1) * stride];
            for (int x = 0; x < size; x++)
            {
                int topVal = buffer[x + 1];
                int val = leftVal + topVal - topLeft;
                buffer[(y + 1) * stride + x + 1] = (byte)MathExt.Clamp(val, 0, 255);
            }
        }
    }

    private void WriteMacroBlockHeader(MacroBlockInfo info, int mbx)
    {
        // Write skip coefficient flag
        _encoder.WriteBool(info.CoeffsSkipped, _skipProb);
        
        // Encode Y mode using keyframe Y mode tree
        _encoder.WriteWithTree(WebPConstants.KeyframeYModeTree, 
                               WebPConstants.KeyframeYModeProbs, 
                               (sbyte)info.LumaMode);

        // Set top/left B pred based on luma mode
        var intraMode = info.LumaMode.ToIntraMode();
        for (int i = 0; i < 4; i++)
        {
            _leftBPred[i] = intraMode;
            _topBPred[mbx * 4 + i] = intraMode;
        }

        // Encode UV mode
        _encoder.WriteWithTree(WebPConstants.KeyframeUvModeTree,
                               WebPConstants.KeyframeUvModeProbs,
                               (sbyte)info.ChromaMode);
    }

    private int[] TransformLumaBlock(int mbx, int mby, MacroBlockInfo info)
    {
        const int stride = 21; // 1 + 16 + 4
        int mbWidth = _macroBlockWidth * 16;
        
        var yWithBorder = new byte[stride * stride];
        CreateLumaBorder(yWithBorder, mbx, mby, stride);

        // Apply selected prediction mode
        ApplyLumaPrediction(yWithBorder, 16, stride, info.LumaMode, mby != 0, mbx != 0);

        var lumaBlocks = new int[16 * 16];

        // Process each 4x4 block
        for (int blockY = 0; blockY < 4; blockY++)
        {
            for (int blockX = 0; blockX < 4; blockX++)
            {
                int blockIndex = blockY * 4 * 16 + blockX * 16;
                int borderIndex = (blockY * 4 + 1) * stride + blockX * 4 + 1;
                int dataIndex = (mby * 16 + blockY * 4) * mbWidth + mbx * 16 + blockX * 4;

                var block = new int[16];
                for (int y = 0; y < 4; y++)
                {
                    for (int x = 0; x < 4; x++)
                    {
                        byte predicted = yWithBorder[borderIndex + y * stride + x];
                        byte actual = _yBuf[dataIndex + y * mbWidth + x];
                        block[y * 4 + x] = actual - predicted;
                    }
                }

                // Apply DCT
                DctTransform.Dct4x4(block);

                Array.Copy(block, 0, lumaBlocks, blockIndex, 16);
            }
        }

        // Reconstruct the pixels as the decoder would see them
        // This ensures borders match what the decoder will have
        var dequantizedBlocks = DequantizeLumaBlocks(lumaBlocks);
        
        // Add dequantized residual back to prediction
        for (int blockY = 0; blockY < 4; blockY++)
        {
            for (int blockX = 0; blockX < 4; blockX++)
            {
                int blockIndex = (blockX + blockY * 4) * 16;
                int y0 = 1 + blockY * 4;
                int x0 = 1 + blockX * 4;
                AddResidue(yWithBorder, dequantizedBlocks, blockIndex, y0, x0, stride);
            }
        }

        // Update borders from reconstructed pixels
        for (int y = 0; y < 17; y++)
        {
            _leftBorderY[y] = yWithBorder[y * stride + 16];
        }
        for (int x = 0; x < 16; x++)
        {
            _topBorderY[mbx * 16 + x] = yWithBorder[16 * stride + x + 1];
        }

        return lumaBlocks;
    }

    /// <summary>
    /// Dequantizes luma blocks to simulate what decoder will reconstruct.
    /// </summary>
    private int[] DequantizeLumaBlocks(int[] lumaBlocks)
    {
        var result = new int[16 * 16];
        
        // First extract and process Y2 (DC coefficients)
        var y2Coeffs = new int[16];
        for (int i = 0; i < 16; i++)
        {
            y2Coeffs[i] = lumaBlocks[i * 16];
        }
        
        // WHT transform on DC coefficients
        WhtTransform.Wht4x4(y2Coeffs);
        
        // Quantize and dequantize Y2 (truncation, matches reference)
        for (int i = 0; i < 16; i++)
        {
            short quant = i > 0 ? _segment.Y2Ac : _segment.Y2Dc;
            y2Coeffs[i] = (y2Coeffs[i] / quant) * quant;
        }
        
        // Inverse WHT
        WhtTransform.Iwht4x4(y2Coeffs);
        
        // Process each 4x4 Y block
        for (int k = 0; k < 16; k++)
        {
            var block = new int[16];
            Array.Copy(lumaBlocks, k * 16, block, 0, 16);
            
            // Quantize and dequantize AC coefficients (skip DC at index 0)
            for (int i = 1; i < 16; i++)
            {
                block[i] = (block[i] / _segment.YAc) * _segment.YAc;
            }
            
            // Set DC from Y2
            block[0] = y2Coeffs[k];
            
            // Inverse DCT
            DctTransform.Idct4x4(block);
            
            Array.Copy(block, 0, result, k * 16, 16);
        }
        
        return result;
    }

    /// <summary>
    /// Adds residual block to prediction buffer.
    /// </summary>
    private static void AddResidue(byte[] buffer, int[] residue, int residueOffset, int y0, int x0, int stride)
    {
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                int bufIdx = (y0 + y) * stride + x0 + x;
                int resIdx = residueOffset + y * 4 + x;
                int val = buffer[bufIdx] + residue[resIdx];
                buffer[bufIdx] = (byte)MathExt.Clamp(val, 0, 255);
            }
        }
    }

    private (int[], int[]) TransformChromaBlocks(int mbx, int mby, ChromaMode chromaMode)
    {
        const int stride = 9; // 1 + 8
        int chromaWidth = _macroBlockWidth * 8;

        var uWithBorder = new byte[stride * stride];
        var vWithBorder = new byte[stride * stride];
        
        CreateChromaBorder(uWithBorder, mbx, mby, _topBorderU, _leftBorderU);
        CreateChromaBorder(vWithBorder, mbx, mby, _topBorderV, _leftBorderV);

        // Apply selected prediction mode
        ApplyChromaPrediction(uWithBorder, 8, stride, chromaMode, mby != 0, mbx != 0);
        ApplyChromaPrediction(vWithBorder, 8, stride, chromaMode, mby != 0, mbx != 0);

        var uBlocks = new int[16 * 4];
        var vBlocks = new int[16 * 4];

        // Process each 4x4 block
        for (int blockY = 0; blockY < 2; blockY++)
        {
            for (int blockX = 0; blockX < 2; blockX++)
            {
                int blockIndex = blockY * 2 * 16 + blockX * 16;
                int borderIndex = (blockY * 4 + 1) * stride + blockX * 4 + 1;
                int dataIndex = (mby * 8 + blockY * 4) * chromaWidth + mbx * 8 + blockX * 4;

                var uBlock = new int[16];
                var vBlock = new int[16];
                
                for (int y = 0; y < 4; y++)
                {
                    for (int x = 0; x < 4; x++)
                    {
                        byte predU = uWithBorder[borderIndex + y * stride + x];
                        byte actU = _uBuf[dataIndex + y * chromaWidth + x];
                        uBlock[y * 4 + x] = actU - predU;

                        byte predV = vWithBorder[borderIndex + y * stride + x];
                        byte actV = _vBuf[dataIndex + y * chromaWidth + x];
                        vBlock[y * 4 + x] = actV - predV;
                    }
                }

                DctTransform.Dct4x4(uBlock);
                DctTransform.Dct4x4(vBlock);

                Array.Copy(uBlock, 0, uBlocks, blockIndex, 16);
                Array.Copy(vBlock, 0, vBlocks, blockIndex, 16);
            }
        }

        // Reconstruct chroma pixels as decoder would see them
        var uDequant = DequantizeChromaBlocks(uBlocks);
        var vDequant = DequantizeChromaBlocks(vBlocks);
        
        // Add dequantized residual back to prediction
        for (int blockY = 0; blockY < 2; blockY++)
        {
            for (int blockX = 0; blockX < 2; blockX++)
            {
                int blockIndex = (blockX + blockY * 2) * 16;
                int y0 = 1 + blockY * 4;
                int x0 = 1 + blockX * 4;
                AddResidue(uWithBorder, uDequant, blockIndex, y0, x0, stride);
                AddResidue(vWithBorder, vDequant, blockIndex, y0, x0, stride);
            }
        }

        // Update borders from reconstructed pixels
        for (int y = 0; y < 9; y++)
        {
            _leftBorderU[y] = uWithBorder[y * stride + 8];
            _leftBorderV[y] = vWithBorder[y * stride + 8];
        }
        for (int x = 0; x < 8; x++)
        {
            _topBorderU[mbx * 8 + x] = uWithBorder[8 * stride + x + 1];
            _topBorderV[mbx * 8 + x] = vWithBorder[8 * stride + x + 1];
        }

        return (uBlocks, vBlocks);
    }

    /// <summary>
    /// Dequantizes chroma blocks to simulate what decoder will reconstruct.
    /// </summary>
    private int[] DequantizeChromaBlocks(int[] chromaBlocks)
    {
        var result = new int[16 * 4];
        
        for (int k = 0; k < 4; k++)
        {
            var block = new int[16];
            Array.Copy(chromaBlocks, k * 16, block, 0, 16);
            
            // Quantize and dequantize all coefficients
            for (int i = 0; i < 16; i++)
            {
                short quant = i > 0 ? _segment.UvAc : _segment.UvDc;
                block[i] = (block[i] / quant) * quant;
            }
            
            // Inverse DCT
            DctTransform.Idct4x4(block);
            
            Array.Copy(block, 0, result, k * 16, 16);
        }
        
        return result;
    }

    private void EncodeResidualData(MacroBlockInfo info, int partitionIndex, int mbx,
                                     int[] yBlocks, int[] uBlocks, int[] vBlocks)
    {
        var plane = info.LumaMode == LumaMode.B ? Plane.YCoeff0 : Plane.Y2;

        // Y2 (if not B mode)
        if (plane == Plane.Y2)
        {
            var coeffs0 = new int[16];
            for (int i = 0; i < 16; i++)
            {
                coeffs0[i] = yBlocks[i * 16];
            }

            WhtTransform.Wht4x4(coeffs0);

            int complexity = _leftComplexity.Y2 + _topComplexity[mbx].Y2;
            bool hasCoeffs = EncodeCoefficients(coeffs0, partitionIndex, plane, complexity,
                                                _segment.Y2Dc, _segment.Y2Ac);

            _leftComplexity.Y2 = hasCoeffs ? (byte)1 : (byte)0;
            _topComplexity[mbx].Y2 = hasCoeffs ? (byte)1 : (byte)0;

            plane = Plane.YCoeff1;
        }

        // Y blocks
        for (int y = 0; y < 4; y++)
        {
            byte left = _leftComplexity.Y[y];
            for (int x = 0; x < 4; x++)
            {
                var block = new int[16];
                Array.Copy(yBlocks, y * 4 * 16 + x * 16, block, 0, 16);

                byte top = _topComplexity[mbx].Y[x];
                int complexity = left + top;

                bool hasCoeffs = EncodeCoefficients(block, partitionIndex, plane, complexity,
                                                    _segment.YDc, _segment.YAc);

                left = hasCoeffs ? (byte)1 : (byte)0;
                _topComplexity[mbx].Y[x] = hasCoeffs ? (byte)1 : (byte)0;
            }
            _leftComplexity.Y[y] = left;
        }

        plane = Plane.Chroma;

        // U blocks
        for (int y = 0; y < 2; y++)
        {
            byte left = _leftComplexity.U[y];
            for (int x = 0; x < 2; x++)
            {
                var block = new int[16];
                Array.Copy(uBlocks, y * 2 * 16 + x * 16, block, 0, 16);

                byte top = _topComplexity[mbx].U[x];
                int complexity = left + top;

                bool hasCoeffs = EncodeCoefficients(block, partitionIndex, plane, complexity,
                                                    _segment.UvDc, _segment.UvAc);

                left = hasCoeffs ? (byte)1 : (byte)0;
                _topComplexity[mbx].U[x] = hasCoeffs ? (byte)1 : (byte)0;
            }
            _leftComplexity.U[y] = left;
        }

        // V blocks
        for (int y = 0; y < 2; y++)
        {
            byte left = _leftComplexity.V[y];
            for (int x = 0; x < 2; x++)
            {
                var block = new int[16];
                Array.Copy(vBlocks, y * 2 * 16 + x * 16, block, 0, 16);

                byte top = _topComplexity[mbx].V[x];
                int complexity = left + top;

                bool hasCoeffs = EncodeCoefficients(block, partitionIndex, plane, complexity,
                                                    _segment.UvDc, _segment.UvAc);

                left = hasCoeffs ? (byte)1 : (byte)0;
                _topComplexity[mbx].V[x] = hasCoeffs ? (byte)1 : (byte)0;
            }
            _leftComplexity.V[y] = left;
        }
    }

    private bool EncodeCoefficients(int[] block, int partitionIndex, Plane plane,
                                     int complexity, short dcQuant, short acQuant)
    {
        var encoder = _partitions[partitionIndex];
        int firstCoeff = plane == Plane.YCoeff1 ? 1 : 0;
        var probs = WebPConstants.CoeffProbs[(int)plane];

        // Quantize and zigzag (truncation, matches reference)
        var zigzagBlock = new int[16];
        for (int i = firstCoeff; i < 16; i++)
        {
            int zigzagIndex = WebPConstants.Zigzag[i];
            int quant = zigzagIndex > 0 ? acQuant : dcQuant;
            zigzagBlock[i] = block[zigzagIndex] / quant;
        }

        // Find end of block
        int eobIndex = 0;
        for (int i = 15; i >= firstCoeff; i--)
        {
            if (zigzagBlock[i] != 0)
            {
                eobIndex = i + 1;
                break;
            }
        }

        bool skipEob = false;

        for (int index = firstCoeff; index < eobIndex; index++)
        {
            int coeff = zigzagBlock[index];
            int band = WebPConstants.CoeffBands[index];
            var probabilities = probs[band][Math.Min(complexity, 2)];
            int startIndex = skipEob ? 2 : 0;

            int absCoeff = Math.Abs(coeff);
            sbyte token;

            if (absCoeff == 0)
            {
                encoder.WriteWithTreeStartIndex(WebPConstants.DctTokenTree, probabilities,
                                                 WebPConstants.Dct0, startIndex);
                skipEob = true;
                token = WebPConstants.Dct0;
            }
            else if (absCoeff <= 4)
            {
                encoder.WriteWithTreeStartIndex(WebPConstants.DctTokenTree, probabilities,
                                                 (sbyte)absCoeff, startIndex);
                skipEob = false;
                token = (sbyte)absCoeff;
            }
            else
            {
                sbyte category;
                int extraBits;
                int extraBase;
                byte[] catProbs;

                if (absCoeff <= 6)
                {
                    category = WebPConstants.DctCat1;
                    extraBits = absCoeff - 5;
                    catProbs = WebPConstants.ProbDctCat[0];
                }
                else if (absCoeff <= 10)
                {
                    category = WebPConstants.DctCat2;
                    extraBits = absCoeff - 7;
                    catProbs = WebPConstants.ProbDctCat[1];
                }
                else if (absCoeff <= 18)
                {
                    category = WebPConstants.DctCat3;
                    extraBits = absCoeff - 11;
                    catProbs = WebPConstants.ProbDctCat[2];
                }
                else if (absCoeff <= 34)
                {
                    category = WebPConstants.DctCat4;
                    extraBits = absCoeff - 19;
                    catProbs = WebPConstants.ProbDctCat[3];
                }
                else if (absCoeff <= 66)
                {
                    category = WebPConstants.DctCat5;
                    extraBits = absCoeff - 35;
                    catProbs = WebPConstants.ProbDctCat[4];
                }
                else
                {
                    category = WebPConstants.DctCat6;
                    extraBits = absCoeff - 67;
                    catProbs = WebPConstants.ProbDctCat[5];
                }

                encoder.WriteWithTreeStartIndex(WebPConstants.DctTokenTree, probabilities,
                                                 category, startIndex);

                // Write extra bits
                int numExtraBits = category == WebPConstants.DctCat6 ? 11 : category - WebPConstants.DctCat1 + 1;
                int mask = 1 << (numExtraBits - 1);
                
                foreach (byte prob in catProbs)
                {
                    if (prob == 0) break;
                    encoder.WriteBool((extraBits & mask) != 0, prob);
                    mask >>= 1;
                }

                skipEob = false;
                token = category;
            }

            // Write sign if non-zero
            if (token != WebPConstants.Dct0)
            {
                encoder.WriteFlag(coeff < 0);
            }

            complexity = token switch
            {
                0 => 0,
                1 => 1,
                _ => 2
            };
        }

        // Write EOB
        if (eobIndex < 16)
        {
            int bandIndex = Math.Max(firstCoeff, eobIndex);
            int band = WebPConstants.CoeffBands[bandIndex];
            var probabilities = probs[band][Math.Min(complexity, 2)];
            encoder.WriteWithTree(WebPConstants.DctTokenTree, probabilities, WebPConstants.DctEob);
        }

        return eobIndex > 0;
    }

    private void WriteUncompressedHeader(uint partitionSize)
    {
        // Frame tag
        uint tag = (partitionSize << 5) | (1 << 4) | 0; // for_display=1, version=0, keyframe=0
        WriteUInt24(tag);

        // Magic bytes for keyframe
        _writer.WriteByte(0x9D);
        _writer.WriteByte(0x01);
        _writer.WriteByte(0x2A);

        // Width and height
        WriteUInt16((ushort)_width);
        WriteUInt16((ushort)_height);
    }

    private void WritePartitions()
    {
        // Write partition data
        foreach (var partition in _partitions)
        {
            byte[] data = partition.FlushAndGetBuffer();
            _writer.Write(data, 0, data.Length);
        }
    }

    private void CreateLumaBorder(byte[] buffer, int mbx, int mby, int stride)
    {
        // Top border
        if (mby > 0)
        {
            for (int x = 0; x < 16; x++)
                buffer[x + 1] = _topBorderY[mbx * 16 + x];
            // Extra 4 pixels for 4x4 prediction
            for (int x = 0; x < 4; x++)
                buffer[17 + x] = (mbx < _macroBlockWidth - 1) ? _topBorderY[mbx * 16 + 16 + x] : _topBorderY[mbx * 16 + 15];
        }
        else
        {
            for (int x = 0; x < 21; x++)
                buffer[x] = 127;
        }

        // Copy top border to other rows for 4x4 block prediction
        for (int i = 17; i < stride; i++)
        {
            buffer[4 * stride + i] = buffer[i];
            buffer[8 * stride + i] = buffer[i];
            buffer[12 * stride + i] = buffer[i];
        }

        // Left border - uses indices 1..16 from _leftBorderY
        if (mbx > 0)
        {
            for (int y = 0; y < 16; y++)
                buffer[(y + 1) * stride] = _leftBorderY[y + 1];
        }
        else
        {
            for (int y = 0; y < 16; y++)
                buffer[(y + 1) * stride] = 129;
        }

        // Top-left corner - uses index 0 from _leftBorderY
        if (mby == 0)
            buffer[0] = 127;
        else if (mbx == 0)
            buffer[0] = 129;
        else
            buffer[0] = _leftBorderY[0];
    }

    private void CreateChromaBorder(byte[] buffer, int mbx, int mby, byte[] topBorder, byte[] leftBorder)
    {
        const int stride = 9;

        // Top
        if (mby > 0)
        {
            for (int x = 0; x < 8; x++)
                buffer[x + 1] = topBorder[mbx * 8 + x];
        }
        else
        {
            for (int x = 0; x < 9; x++)
                buffer[x] = 127;
        }

        // Left - uses indices 1..8 from leftBorder
        if (mbx > 0)
        {
            for (int y = 0; y < 8; y++)
                buffer[(y + 1) * stride] = leftBorder[y + 1];
        }
        else
        {
            for (int y = 0; y < 8; y++)
                buffer[(y + 1) * stride] = 129;
        }

        // Top-left corner - uses index 0 from leftBorder
        if (mby == 0)
            buffer[0] = 127;
        else if (mbx == 0)
            buffer[0] = 129;
        else
            buffer[0] = leftBorder[0];
    }

    private void PredictDC(byte[] buffer, int size, int stride, bool hasTop, bool hasLeft)
    {
        int sum = 0;
        int count = 0;

        if (hasTop)
        {
            for (int x = 0; x < size; x++)
                sum += buffer[x + 1];
            count += size;
        }

        if (hasLeft)
        {
            for (int y = 0; y < size; y++)
                sum += buffer[(y + 1) * stride];
            count += size;
        }

        byte dc = count > 0 ? (byte)((sum + count / 2) / count) : (byte)128;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                buffer[(y + 1) * stride + x + 1] = dc;
            }
        }
    }

    private void WriteUInt16(ushort value)
    {
        _writer.WriteByte((byte)(value & 0xFF));
        _writer.WriteByte((byte)((value >> 8) & 0xFF));
    }

    private void WriteUInt24(uint value)
    {
        _writer.WriteByte((byte)(value & 0xFF));
        _writer.WriteByte((byte)((value >> 8) & 0xFF));
        _writer.WriteByte((byte)((value >> 16) & 0xFF));
    }

    private struct Segment
    {
        public short YDc;
        public short YAc;
        public short Y2Dc;
        public short Y2Ac;
        public short UvDc;
        public short UvAc;
    }

    private struct Complexity
    {
        public byte Y2;
        public byte[] Y;
        public byte[] U;
        public byte[] V;

        public Complexity()
        {
            Y2 = 0;
            Y = new byte[4];
            U = new byte[2];
            V = new byte[2];
        }

        public void Clear(bool includeY2)
        {
            Array.Clear(Y, 0, 4);
            Array.Clear(U, 0, 2);
            Array.Clear(V, 0, 2);
            if (includeY2) Y2 = 0;
        }
    }

    private struct MacroBlockInfo
    {
        public LumaMode LumaMode;
        public ChromaMode ChromaMode;
        public bool CoeffsSkipped;
    }

    internal enum LumaMode : sbyte
    {
        DC = 0,
        V = 1,
        H = 2,
        TM = 3,
        B = 4
    }

    internal enum ChromaMode : sbyte
    {
        DC = 0,
        V = 1,
        H = 2,
        TM = 3
    }

    internal enum IntraMode
    {
        DC = 0,
        TM = 1,
        VE = 2,
        HE = 3,
        LD = 4,
        RD = 5,
        VR = 6,
        VL = 7,
        HD = 8,
        HU = 9
    }

}

internal static class LumaModeExtensions
{
    public static VP8Encoder.IntraMode ToIntraMode(this VP8Encoder.LumaMode mode)
    {
        return mode switch
        {
            VP8Encoder.LumaMode.DC => VP8Encoder.IntraMode.DC,
            VP8Encoder.LumaMode.V => VP8Encoder.IntraMode.VE,
            VP8Encoder.LumaMode.H => VP8Encoder.IntraMode.HE,
            VP8Encoder.LumaMode.TM => VP8Encoder.IntraMode.TM,
            _ => VP8Encoder.IntraMode.DC
        };
    }
}
