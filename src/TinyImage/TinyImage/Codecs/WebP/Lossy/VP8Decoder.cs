using System;
using System.IO;
using TinyImage.Codecs.WebP.Core;

namespace TinyImage.Codecs.WebP.Lossy;

/// <summary>
/// VP8 lossy decoder for WebP images.
/// Only decodes keyframes (intra-predicted frames).
/// Translated from webp-rust vp8.rs
/// </summary>
internal class VP8Decoder
{
    private readonly Stream _stream;
    private readonly ArithmeticDecoder _decoder;
    private ushort _mbWidth;
    private ushort _mbHeight;
    private MacroBlock[] _macroblocks;
    private VP8Frame _frame;

    private bool _segmentsEnabled;
    private bool _segmentsUpdateMap;
    private Segment[] _segments;

    private bool _loopFilterAdjustmentsEnabled;
    private int[] _refDelta;
    private int[] _modeDelta;

    private ArithmeticDecoder[] _partitions;
    private byte _numPartitions;

    private byte[][][] _segmentTreeProbs;
    private byte[][][][] _tokenProbs;
    private byte? _probSkipFalse;

    private PreviousMacroBlock[] _top;
    private PreviousMacroBlock _left;

    private byte[] _topBorderY;
    private byte[] _leftBorderY;
    private byte[] _topBorderU;
    private byte[] _leftBorderU;
    private byte[] _topBorderV;
    private byte[] _leftBorderV;

    /// <summary>
    /// Creates a new VP8 decoder.
    /// </summary>
    public VP8Decoder(Stream stream)
    {
        _stream = stream;
        _decoder = new ArithmeticDecoder();
        _macroblocks = Array.Empty<MacroBlock>();
        _frame = new VP8Frame();
        _segments = new Segment[WebPConstants.MaxSegments];
        _refDelta = new int[4];
        _modeDelta = new int[4];
        _partitions = new ArithmeticDecoder[8];
        for (int i = 0; i < 8; i++)
            _partitions[i] = new ArithmeticDecoder();
        _numPartitions = 1;
        _segmentTreeProbs = CreateSegmentTreeProbs();
        _tokenProbs = CreateTokenProbs();
        _top = Array.Empty<PreviousMacroBlock>();
        _left = new PreviousMacroBlock();
        _topBorderY = Array.Empty<byte>();
        _leftBorderY = Array.Empty<byte>();
        _topBorderU = Array.Empty<byte>();
        _leftBorderU = Array.Empty<byte>();
        _topBorderV = Array.Empty<byte>();
        _leftBorderV = Array.Empty<byte>();
    }

    private static byte[][][] CreateSegmentTreeProbs()
    {
        return new byte[][][] {
            new byte[][] { new byte[] { 255 } },
            new byte[][] { new byte[] { 255 } },
            new byte[][] { new byte[] { 255 } }
        };
    }

    private static byte[][][][] CreateTokenProbs()
    {
        // Deep copy of COEFF_PROBS
        var probs = new byte[4][][][];
        for (int i = 0; i < 4; i++)
        {
            probs[i] = new byte[8][][];
            for (int j = 0; j < 8; j++)
            {
                probs[i][j] = new byte[3][];
                for (int k = 0; k < 3; k++)
                {
                    probs[i][j][k] = new byte[11];
                    Array.Copy(WebPConstants.CoeffProbs[i][j][k], probs[i][j][k], 11);
                }
            }
        }
        return probs;
    }

    /// <summary>
    /// Decodes a VP8 frame from the stream.
    /// </summary>
    public VP8Frame Decode()
    {
        ReadFrameHeader();
        DecodeFrame();
        return _frame;
    }

    private void ReadFrameHeader()
    {
        // Read frame tag (3 bytes)
        byte[] tagBytes = new byte[3];
        if (_stream.Read(tagBytes, 0, 3) != 3)
            throw new WebPDecodingException("Failed to read frame tag");

        uint tag = (uint)(tagBytes[0] | (tagBytes[1] << 8) | (tagBytes[2] << 16));

        bool keyframe = (tag & 1) == 0;
        if (!keyframe)
            throw new WebPDecodingException("Non-keyframe frames are not supported");

        _frame.Version = (byte)((tag >> 1) & 7);
        _frame.ForDisplay = ((tag >> 4) & 1) != 0;
        uint firstPartitionSize = tag >> 5;

        // Read start code
        byte[] startCode = new byte[3];
        if (_stream.Read(startCode, 0, 3) != 3)
            throw new WebPDecodingException("Failed to read start code");

        if (startCode[0] != 0x9d || startCode[1] != 0x01 || startCode[2] != 0x2a)
            throw new WebPDecodingException($"Invalid VP8 magic: {startCode[0]:X2} {startCode[1]:X2} {startCode[2]:X2}");

        // Read dimensions
        byte[] dimBytes = new byte[4];
        if (_stream.Read(dimBytes, 0, 4) != 4)
            throw new WebPDecodingException("Failed to read dimensions");

        ushort w = (ushort)(dimBytes[0] | (dimBytes[1] << 8));
        ushort h = (ushort)(dimBytes[2] | (dimBytes[3] << 8));

        _frame.Width = (ushort)(w & 0x3FFF);
        _frame.Height = (ushort)(h & 0x3FFF);

        _mbWidth = (ushort)((_frame.Width + 15) / 16);
        _mbHeight = (ushort)((_frame.Height + 15) / 16);

        // Initialize prediction state
        _top = new PreviousMacroBlock[_mbWidth];
        for (int i = 0; i < _mbWidth; i++)
            _top[i] = new PreviousMacroBlock();
        _left = new PreviousMacroBlock();

        // Allocate YUV buffers
        _frame.YBuffer = new byte[_mbWidth * 16 * _mbHeight * 16];
        _frame.UBuffer = new byte[_mbWidth * 8 * _mbHeight * 8];
        _frame.VBuffer = new byte[_mbWidth * 8 * _mbHeight * 8];

        // Initialize borders
        _topBorderY = new byte[_frame.Width + 4 + 16];
        _leftBorderY = new byte[1 + 16];
        _topBorderU = new byte[8 * _mbWidth];
        _leftBorderU = new byte[1 + 8];
        _topBorderV = new byte[8 * _mbWidth];
        _leftBorderV = new byte[1 + 8];

        for (int i = 0; i < _topBorderY.Length; i++) _topBorderY[i] = 127;
        for (int i = 0; i < _leftBorderY.Length; i++) _leftBorderY[i] = 129;
        for (int i = 0; i < _topBorderU.Length; i++) _topBorderU[i] = 127;
        for (int i = 0; i < _leftBorderU.Length; i++) _leftBorderU[i] = 129;
        for (int i = 0; i < _topBorderV.Length; i++) _topBorderV[i] = 127;
        for (int i = 0; i < _leftBorderV.Length; i++) _leftBorderV[i] = 129;

        // Read first partition
        byte[] firstPartitionData = new byte[firstPartitionSize];
        if (_stream.Read(firstPartitionData, 0, (int)firstPartitionSize) != (int)firstPartitionSize)
            throw new WebPDecodingException("Failed to read first partition");

        _decoder.Init(firstPartitionData, (int)firstPartitionSize);

        // Read color space and pixel type
        byte colorSpace = _decoder.ReadLiteral(1);
        _frame.PixelType = _decoder.ReadLiteral(1);

        if (colorSpace != 0)
            throw new WebPDecodingException($"Invalid color space: {colorSpace}");

        // Segment updates
        _segmentsEnabled = _decoder.ReadFlag();
        if (_segmentsEnabled)
            ReadSegmentUpdates();

        // Filter parameters
        _frame.FilterType = _decoder.ReadFlag();
        _frame.FilterLevel = _decoder.ReadLiteral(6);
        _frame.SharpnessLevel = _decoder.ReadLiteral(3);

        // Loop filter adjustments
        _loopFilterAdjustmentsEnabled = _decoder.ReadFlag();
        if (_loopFilterAdjustmentsEnabled)
            ReadLoopFilterAdjustments();

        // Number of partitions
        int numPartitions = 1 << _decoder.ReadLiteral(2);
        _numPartitions = (byte)numPartitions;

        // Initialize partitions
        InitPartitions(numPartitions);

        // Quantization indices
        ReadQuantizationIndices();

        // Refresh entropy probs (skip)
        _decoder.ReadLiteral(1);

        // Token probabilities
        UpdateTokenProbabilities();

        // Skip coefficient flag
        byte mbNoSkipCoeff = _decoder.ReadLiteral(1);
        _probSkipFalse = mbNoSkipCoeff == 1 ? (byte?)_decoder.ReadLiteral(8) : null;
    }

    private void ReadSegmentUpdates()
    {
        _segmentsUpdateMap = _decoder.ReadFlag();
        bool updateSegmentFeatureData = _decoder.ReadFlag();

        if (updateSegmentFeatureData)
        {
            bool segmentFeatureMode = _decoder.ReadFlag();

            for (int i = 0; i < WebPConstants.MaxSegments; i++)
                _segments[i].DeltaValues = !segmentFeatureMode;

            for (int i = 0; i < WebPConstants.MaxSegments; i++)
                _segments[i].QuantizerLevel = (sbyte)_decoder.ReadOptionalSignedValue(7);

            for (int i = 0; i < WebPConstants.MaxSegments; i++)
                _segments[i].LoopFilterLevel = (sbyte)_decoder.ReadOptionalSignedValue(6);
        }

        if (_segmentsUpdateMap)
        {
            for (int i = 0; i < 3; i++)
            {
                bool update = _decoder.ReadFlag();
                byte prob = update ? _decoder.ReadLiteral(8) : (byte)255;
                _segmentTreeProbs[i][0][0] = prob;
            }
        }
    }

    private void ReadLoopFilterAdjustments()
    {
        if (_decoder.ReadFlag())
        {
            for (int i = 0; i < 4; i++)
                _refDelta[i] = _decoder.ReadOptionalSignedValue(6);

            for (int i = 0; i < 4; i++)
                _modeDelta[i] = _decoder.ReadOptionalSignedValue(6);
        }
    }

    private void InitPartitions(int n)
    {
        if (n > 1)
        {
            byte[] sizes = new byte[3 * (n - 1)];
            if (_stream.Read(sizes, 0, sizes.Length) != sizes.Length)
                throw new WebPDecodingException("Failed to read partition sizes");

            for (int i = 0; i < n - 1; i++)
            {
                int size = sizes[i * 3] | (sizes[i * 3 + 1] << 8) | (sizes[i * 3 + 2] << 16);
                byte[] data = new byte[size];
                if (_stream.Read(data, 0, size) != size)
                    throw new WebPDecodingException("Failed to read partition data");
                _partitions[i].Init(data, size);
            }
        }

        // Read last partition (rest of stream)
        using (var ms = new MemoryStream())
        {
            _stream.CopyTo(ms);
            byte[] data = ms.ToArray();
            _partitions[n - 1].Init(data, data.Length);
        }
    }

    private void ReadQuantizationIndices()
    {
        int yacAbs = _decoder.ReadLiteral(7);
        int ydcDelta = _decoder.ReadOptionalSignedValue(4);
        int y2dcDelta = _decoder.ReadOptionalSignedValue(4);
        int y2acDelta = _decoder.ReadOptionalSignedValue(4);
        int uvdcDelta = _decoder.ReadOptionalSignedValue(4);
        int uvacDelta = _decoder.ReadOptionalSignedValue(4);

        int n = _segmentsEnabled ? WebPConstants.MaxSegments : 1;
        for (int i = 0; i < n; i++)
        {
            int baseQ;
            if (_segmentsEnabled)
            {
                if (_segments[i].DeltaValues)
                    baseQ = _segments[i].QuantizerLevel + yacAbs;
                else
                    baseQ = _segments[i].QuantizerLevel;
            }
            else
            {
                baseQ = yacAbs;
            }

            _segments[i].Ydc = DcQuant(baseQ + ydcDelta);
            _segments[i].Yac = AcQuant(baseQ);
            _segments[i].Y2dc = (short)(DcQuant(baseQ + y2dcDelta) * 2);
            _segments[i].Y2ac = (short)(AcQuant(baseQ + y2acDelta) * 155 / 100);
            _segments[i].Uvdc = DcQuant(baseQ + uvdcDelta);
            _segments[i].Uvac = AcQuant(baseQ + uvacDelta);

            if (_segments[i].Y2ac < 8) _segments[i].Y2ac = 8;
            if (_segments[i].Uvdc > 132) _segments[i].Uvdc = 132;
        }
    }

    private static short DcQuant(int index)
    {
        return WebPConstants.DcQuant[Math.Max(0, Math.Min(127, index))];
    }

    private static short AcQuant(int index)
    {
        return WebPConstants.AcQuant[Math.Max(0, Math.Min(127, index))];
    }

    private void UpdateTokenProbabilities()
    {
        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < 8; j++)
            {
                for (int k = 0; k < 3; k++)
                {
                    for (int t = 0; t < 11; t++)
                    {
                        if (_decoder.ReadBool(WebPConstants.CoeffUpdateProbs[i][j][k][t]))
                        {
                            _tokenProbs[i][j][k][t] = _decoder.ReadLiteral(8);
                        }
                    }
                }
            }
        }
    }

    private void DecodeFrame()
    {
        _macroblocks = new MacroBlock[_mbWidth * _mbHeight];

        for (int mby = 0; mby < _mbHeight; mby++)
        {
            int p = mby % _numPartitions;
            _left = new PreviousMacroBlock();

            for (int mbx = 0; mbx < _mbWidth; mbx++)
            {
                var mb = ReadMacroblockHeader(mbx);

                int[] blocks;
                if (!mb.CoeffsSkipped)
                {
                    blocks = ReadResidualData(ref mb, mbx, p);
                }
                else
                {
                    if (mb.LumaMode != LumaMode.B)
                    {
                        _left.Complexity[0] = 0;
                        _top[mbx].Complexity[0] = 0;
                    }

                    for (int i = 1; i < 9; i++)
                    {
                        _left.Complexity[i] = 0;
                        _top[mbx].Complexity[i] = 0;
                    }

                    blocks = new int[384];
                }

                IntraPredictLuma(mbx, mby, ref mb, blocks);
                IntraPredictChroma(mbx, mby, ref mb, blocks);

                _macroblocks[mby * _mbWidth + mbx] = mb;
            }

            // Reset left borders for next row
            _leftBorderY = new byte[1 + 16];
            for (int i = 0; i < _leftBorderY.Length; i++) _leftBorderY[i] = 129;
            _leftBorderU = new byte[1 + 8];
            for (int i = 0; i < _leftBorderU.Length; i++) _leftBorderU[i] = 129;
            _leftBorderV = new byte[1 + 8];
            for (int i = 0; i < _leftBorderV.Length; i++) _leftBorderV[i] = 129;
        }

        // Apply loop filter
        for (int mby = 0; mby < _mbHeight; mby++)
        {
            for (int mbx = 0; mbx < _mbWidth; mbx++)
            {
                var mb = _macroblocks[mby * _mbWidth + mbx];
                ApplyLoopFilter(mbx, mby, ref mb);
            }
        }
    }

    private MacroBlock ReadMacroblockHeader(int mbx)
    {
        var mb = new MacroBlock();

        if (_segmentsEnabled && _segmentsUpdateMap)
        {
            mb.SegmentId = (byte)ReadSegmentId();
        }

        if (_probSkipFalse.HasValue)
        {
            mb.CoeffsSkipped = _decoder.ReadBool(_probSkipFalse.Value);
        }

        // Read luma prediction mode
        int luma = ReadLumaMode();
        mb.LumaMode = (LumaMode)luma;

        if (mb.LumaMode == LumaMode.B)
        {
            // Read individual B prediction modes
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    int top = (int)_top[mbx].BPred[x];
                    int left = (int)_left.BPred[y];
                    int intra = ReadBPredMode(top, left);
                    mb.BPred[x + y * 4] = (IntraMode)intra;
                    _top[mbx].BPred[x] = (IntraMode)intra;
                    _left.BPred[y] = (IntraMode)intra;
                }
            }
        }
        else
        {
            IntraMode mode = LumaModeToIntra(mb.LumaMode);
            for (int i = 0; i < 4; i++)
            {
                mb.BPred[12 + i] = mode;
                _left.BPred[i] = mode;
            }
        }

        // Read chroma prediction mode
        int chroma = ReadChromaMode();
        mb.ChromaMode = (ChromaMode)chroma;

        // Update top prediction state
        for (int i = 0; i < 4; i++)
            _top[mbx].BPred[i] = mb.BPred[12 + i];

        return mb;
    }

    private static IntraMode LumaModeToIntra(LumaMode mode)
    {
        return mode switch
        {
            LumaMode.DC => IntraMode.DC,
            LumaMode.V => IntraMode.VE,
            LumaMode.H => IntraMode.HE,
            LumaMode.TM => IntraMode.TM,
            _ => IntraMode.DC
        };
    }

    private int ReadSegmentId()
    {
        // Simple tree read for segment ID
        if (!_decoder.ReadBool(_segmentTreeProbs[0][0][0]))
        {
            return _decoder.ReadBool(_segmentTreeProbs[1][0][0]) ? 1 : 0;
        }
        else
        {
            return _decoder.ReadBool(_segmentTreeProbs[2][0][0]) ? 3 : 2;
        }
    }

    private int ReadLumaMode()
    {
        // Keyframe Y mode tree
        if (!_decoder.ReadBool(WebPConstants.KeyframeYModeProbs[0]))
            return 4; // B_PRED

        if (!_decoder.ReadBool(WebPConstants.KeyframeYModeProbs[1]))
        {
            return _decoder.ReadBool(WebPConstants.KeyframeYModeProbs[2]) ? 1 : 0; // V_PRED or DC_PRED
        }
        else
        {
            return _decoder.ReadBool(WebPConstants.KeyframeYModeProbs[3]) ? 3 : 2; // TM_PRED or H_PRED
        }
    }

    private int ReadChromaMode()
    {
        if (!_decoder.ReadBool(WebPConstants.KeyframeUvModeProbs[0]))
            return 0; // DC_PRED

        if (!_decoder.ReadBool(WebPConstants.KeyframeUvModeProbs[1]))
            return 1; // V_PRED

        return _decoder.ReadBool(WebPConstants.KeyframeUvModeProbs[2]) ? 3 : 2; // TM_PRED or H_PRED
    }

    private int ReadBPredMode(int top, int left)
    {
        byte[] probs = WebPConstants.KeyframeBPredModeProbs[top][left];
        sbyte[] tree = WebPConstants.KeyframeBPredModeTree;

        int index = 0;
        while (true)
        {
            bool b = _decoder.ReadBool(probs[index / 2]);
            int nextIndex = b ? tree[index + 1] : tree[index];

            if (nextIndex <= 0)
                return -nextIndex;

            index = nextIndex;
        }
    }

    private int[] ReadResidualData(ref MacroBlock mb, int mbx, int p)
    {
        int sindex = mb.SegmentId;
        int[] blocks = new int[384];

        Plane plane = mb.LumaMode == LumaMode.B ? Plane.YCoeff0 : Plane.Y2;

        if (plane == Plane.Y2)
        {
            int complexity = _top[mbx].Complexity[0] + _left.Complexity[0];
            int[] block = new int[16];
            short dcq = _segments[sindex].Y2dc;
            short acq = _segments[sindex].Y2ac;
            bool n = ReadCoefficients(block, p, plane, complexity, dcq, acq);

            _left.Complexity[0] = (byte)(n ? 1 : 0);
            _top[mbx].Complexity[0] = (byte)(n ? 1 : 0);

            WhtTransform.Iwht4x4(block);

            for (int k = 0; k < 16; k++)
                blocks[16 * k] = block[k];

            plane = Plane.YCoeff1;
        }

        // Read Y coefficients
        for (int y = 0; y < 4; y++)
        {
            byte leftComp = _left.Complexity[y + 1];
            for (int x = 0; x < 4; x++)
            {
                int i = x + y * 4;
                int[] block = new int[16];
                Array.Copy(blocks, i * 16, block, 0, 16);

                int complexity = _top[mbx].Complexity[x + 1] + leftComp;
                short dcq = _segments[sindex].Ydc;
                short acq = _segments[sindex].Yac;

                bool n = ReadCoefficients(block, p, plane, complexity, dcq, acq);

                if (block[0] != 0 || n)
                {
                    mb.NonZeroDct = true;
                    DctTransform.Idct4x4(block);
                }

                Array.Copy(block, 0, blocks, i * 16, 16);

                leftComp = (byte)(n ? 1 : 0);
                _top[mbx].Complexity[x + 1] = (byte)(n ? 1 : 0);
            }
            _left.Complexity[y + 1] = leftComp;
        }

        // Read chroma coefficients
        plane = Plane.Chroma;
        int[] jValues = { 5, 7 };
        foreach (int j in jValues)
        {
            for (int y = 0; y < 2; y++)
            {
                byte leftComp = _left.Complexity[y + j];
                for (int x = 0; x < 2; x++)
                {
                    int i = x + y * 2 + (j == 5 ? 16 : 20);
                    int[] block = new int[16];
                    Array.Copy(blocks, i * 16, block, 0, 16);

                    int complexity = _top[mbx].Complexity[x + j] + leftComp;
                    short dcq = _segments[sindex].Uvdc;
                    short acq = _segments[sindex].Uvac;

                    bool n = ReadCoefficients(block, p, plane, complexity, dcq, acq);

                    if (block[0] != 0 || n)
                    {
                        mb.NonZeroDct = true;
                        DctTransform.Idct4x4(block);
                    }

                    Array.Copy(block, 0, blocks, i * 16, 16);

                    leftComp = (byte)(n ? 1 : 0);
                    _top[mbx].Complexity[x + j] = (byte)(n ? 1 : 0);
                }
                _left.Complexity[y + j] = leftComp;
            }
        }

        return blocks;
    }

    private int _readCoeffCallCount = 0;
    private bool ReadCoefficients(int[] block, int p, Plane plane, int complexity, short dcq, short acq)
    {
        int firstCoeff = plane == Plane.YCoeff1 ? 1 : 0;
        byte[][][] probs = _tokenProbs[(int)plane];
        var decoder = _partitions[p];

        int comp = Math.Min(complexity, 2);
        bool hasCoefficients = false;
        bool skip = false;

        for (int i = firstCoeff; i < 16; i++)
        {
            int band = WebPConstants.CoeffBands[i];
            byte[] tree = probs[band][comp];

            int token = ReadToken(decoder, tree, skip);

            if (token == 11) // DCT_EOB
                break;

            if (token == 0) // DCT_0
            {
                skip = true;
                hasCoefficients = true;
                comp = 0;
                continue;
            }

            skip = false;
            int absValue;

            if (token <= 4)
            {
                absValue = token;
            }
            else
            {
                int category = token - 5;
                byte[] catProbs = WebPConstants.ProbDctCat[category];
                int extra = 0;

                for (int t = 0; t < 12 && catProbs[t] != 0; t++)
                {
                    bool b = decoder.ReadBool(catProbs[t]);
                    extra = extra * 2 + (b ? 1 : 0);
                }

                absValue = WebPConstants.DctCatBase[category] + extra;
            }

            comp = absValue == 0 ? 0 : (absValue == 1 ? 1 : 2);

            if (decoder.ReadSign())
                absValue = -absValue;

            int zigzag = WebPConstants.Zigzag[i];
            block[zigzag] = absValue * (zigzag > 0 ? acq : dcq);
            hasCoefficients = true;
        }

        return hasCoefficients;
    }

    private int ReadToken(ArithmeticDecoder decoder, byte[] probs, bool skip)
    {
        // DCT token tree traversal
        // Tree structure: Node N uses probs[N]
        // Node 0: false→EOB(11), true→Node 1
        // Node 1: false→DCT_0(0), true→Node 2
        // Node 2: false→DCT_1(1), true→Node 3
        // Node 3: false→Node 4, true→Node 6
        // Node 4: false→DCT_2(2), true→Node 5
        // Node 5: false→DCT_3(3), true→DCT_4(4)
        // Node 6: false→Node 7, true→Node 8
        // Node 7: false→DCT_CAT1(5), true→DCT_CAT2(6)
        // Node 8: false→Node 9, true→Node 10
        // Node 9: false→DCT_CAT3(7), true→DCT_CAT4(8)
        // Node 10: false→DCT_CAT5(9), true→DCT_CAT6(10)

        // When skip=true, we start at Node 1 (skip EOB check)
        if (!skip)
        {
            // Node 0: Check for EOB
            if (!decoder.ReadBool(probs[0]))
                return 11; // DCT_EOB
        }

        // Node 1: Check for DCT_0
        if (!decoder.ReadBool(probs[1]))
            return 0; // DCT_0

        // Node 2: Check for DCT_1
        if (!decoder.ReadBool(probs[2]))
            return 1; // DCT_1

        // Node 3: Branch between 2-4 and 5-10
        if (!decoder.ReadBool(probs[3]))
        {
            // Node 4: Check for DCT_2
            if (!decoder.ReadBool(probs[4]))
                return 2; // DCT_2
            // Node 5: DCT_3 or DCT_4
            return decoder.ReadBool(probs[5]) ? 4 : 3;
        }
        else
        {
            // Node 6: Branch between CAT1-2 and CAT3-6
            if (!decoder.ReadBool(probs[6]))
            {
                // Node 7: DCT_CAT1 or DCT_CAT2
                return decoder.ReadBool(probs[7]) ? 6 : 5;
            }
            else
            {
                // Node 8: Branch between CAT3-4 and CAT5-6
                if (!decoder.ReadBool(probs[8]))
                {
                    // Node 9: DCT_CAT3 or DCT_CAT4
                    return decoder.ReadBool(probs[9]) ? 8 : 7;
                }
                else
                {
                    // Node 10: DCT_CAT5 or DCT_CAT6
                    return decoder.ReadBool(probs[10]) ? 10 : 9;
                }
            }
        }
    }

    private void IntraPredictLuma(int mbx, int mby, ref MacroBlock mb, int[] resdata)
    {
        int stride = VP8Prediction.LumaStride;
        int mw = _mbWidth;
        byte[] ws = VP8Prediction.CreateBorderLuma(mbx, mby, mw, _topBorderY, _leftBorderY);

        switch (mb.LumaMode)
        {
            case LumaMode.V:
                VP8Prediction.PredictVPred(ws, 16, 1, 1, stride);
                break;
            case LumaMode.H:
                VP8Prediction.PredictHPred(ws, 16, 1, 1, stride);
                break;
            case LumaMode.TM:
                VP8Prediction.PredictTmPred(ws, 16, 1, 1, stride);
                break;
            case LumaMode.DC:
                VP8Prediction.PredictDcPred(ws, 16, stride, mby != 0, mbx != 0);
                break;
            case LumaMode.B:
                VP8Prediction.Predict4x4(ws, stride, mb.BPred, resdata);
                break;
        }

        if (mb.LumaMode != LumaMode.B)
        {
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    int i = x + y * 4;
                    int y0 = 1 + y * 4;
                    int x0 = 1 + x * 4;
                    VP8Prediction.AddResidue(ws, resdata, i * 16, y0, x0, stride);
                }
            }
        }

        // Update borders
        _leftBorderY[0] = ws[16];
        for (int i = 0; i < 16; i++)
            _leftBorderY[1 + i] = ws[(i + 1) * stride + 16];

        for (int i = 0; i < 16; i++)
            _topBorderY[mbx * 16 + i] = ws[16 * stride + 1 + i];

        // Copy to frame buffer
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                _frame.YBuffer[(mby * 16 + y) * mw * 16 + mbx * 16 + x] = ws[(1 + y) * stride + 1 + x];
            }
        }
    }

    private void IntraPredictChroma(int mbx, int mby, ref MacroBlock mb, int[] resdata)
    {
        int stride = VP8Prediction.ChromaStride;
        int mw = _mbWidth;

        byte[] uws = VP8Prediction.CreateBorderChroma(mbx, mby, _topBorderU, _leftBorderU);
        byte[] vws = VP8Prediction.CreateBorderChroma(mbx, mby, _topBorderV, _leftBorderV);

        switch (mb.ChromaMode)
        {
            case ChromaMode.DC:
                VP8Prediction.PredictDcPred(uws, 8, stride, mby != 0, mbx != 0);
                VP8Prediction.PredictDcPred(vws, 8, stride, mby != 0, mbx != 0);
                break;
            case ChromaMode.V:
                VP8Prediction.PredictVPred(uws, 8, 1, 1, stride);
                VP8Prediction.PredictVPred(vws, 8, 1, 1, stride);
                break;
            case ChromaMode.H:
                VP8Prediction.PredictHPred(uws, 8, 1, 1, stride);
                VP8Prediction.PredictHPred(vws, 8, 1, 1, stride);
                break;
            case ChromaMode.TM:
                VP8Prediction.PredictTmPred(uws, 8, 1, 1, stride);
                VP8Prediction.PredictTmPred(vws, 8, 1, 1, stride);
                break;
        }

        for (int y = 0; y < 2; y++)
        {
            for (int x = 0; x < 2; x++)
            {
                int i = x + y * 2;
                int y0 = 1 + y * 4;
                int x0 = 1 + x * 4;
                VP8Prediction.AddResidue(uws, resdata, (16 + i) * 16, y0, x0, stride);
                VP8Prediction.AddResidue(vws, resdata, (20 + i) * 16, y0, x0, stride);
            }
        }

        VP8Prediction.SetChromaBorder(_leftBorderU, _topBorderU, uws, mbx);
        VP8Prediction.SetChromaBorder(_leftBorderV, _topBorderV, vws, mbx);

        // Copy to frame buffers
        for (int y = 0; y < 8; y++)
        {
            int uvBufIndex = (mby * 8 + y) * mw * 8 + mbx * 8;
            int wsIndex = (1 + y) * stride + 1;

            for (int x = 0; x < 8; x++)
            {
                _frame.UBuffer[uvBufIndex + x] = uws[wsIndex + x];
                _frame.VBuffer[uvBufIndex + x] = vws[wsIndex + x];
            }
        }
    }

    private void ApplyLoopFilter(int mbx, int mby, ref MacroBlock mb)
    {
        int lumaW = _mbWidth * 16;
        int chromaW = _mbWidth * 8;

        var (filterLevel, interiorLimit, hevThreshold) = CalculateFilterParameters(ref mb);

        if (filterLevel == 0) return;

        byte mbedgeLimit = (byte)((filterLevel + 2) * 2 + interiorLimit);
        byte subBedgeLimit = (byte)(filterLevel * 2 + interiorLimit);

        bool doSubblockFiltering = mb.LumaMode == LumaMode.B || (!mb.CoeffsSkipped && mb.NonZeroDct);

        // Filter across left of macroblock
        if (mbx > 0)
        {
            if (_frame.FilterType)
            {
                for (int y = 0; y < 16; y++)
                {
                    int y0 = mby * 16 + y;
                    int x0 = mbx * 16;
                    LoopFilter.SimpleSegmentHorizontal(mbedgeLimit, _frame.YBuffer, y0 * lumaW + x0 - 4);
                }
            }
            else
            {
                for (int y = 0; y < 16; y++)
                {
                    int y0 = mby * 16 + y;
                    int x0 = mbx * 16;
                    LoopFilter.MacroblockFilterHorizontal(hevThreshold, interiorLimit, mbedgeLimit,
                        _frame.YBuffer, y0 * lumaW + x0 - 4);
                }

                for (int y = 0; y < 8; y++)
                {
                    int y0 = mby * 8 + y;
                    int x0 = mbx * 8;
                    LoopFilter.MacroblockFilterHorizontal(hevThreshold, interiorLimit, mbedgeLimit,
                        _frame.UBuffer, y0 * chromaW + x0 - 4);
                    LoopFilter.MacroblockFilterHorizontal(hevThreshold, interiorLimit, mbedgeLimit,
                        _frame.VBuffer, y0 * chromaW + x0 - 4);
                }
            }
        }

        // Filter across vertical subblocks
        if (doSubblockFiltering)
        {
            if (_frame.FilterType)
            {
                for (int x = 4; x < 15; x += 4)
                {
                    for (int y = 0; y < 16; y++)
                    {
                        int y0 = mby * 16 + y;
                        int x0 = mbx * 16 + x;
                        LoopFilter.SimpleSegmentHorizontal(subBedgeLimit, _frame.YBuffer, y0 * lumaW + x0 - 4);
                    }
                }
            }
            else
            {
                for (int x = 4; x < 13; x += 4)
                {
                    for (int y = 0; y < 16; y++)
                    {
                        int y0 = mby * 16 + y;
                        int x0 = mbx * 16 + x;
                        LoopFilter.SubblockFilterHorizontal(hevThreshold, interiorLimit, subBedgeLimit,
                            _frame.YBuffer, y0 * lumaW + x0 - 4);
                    }
                }

                for (int y = 0; y < 8; y++)
                {
                    int y0 = mby * 8 + y;
                    int x0 = mbx * 8 + 4;
                    LoopFilter.SubblockFilterHorizontal(hevThreshold, interiorLimit, subBedgeLimit,
                        _frame.UBuffer, y0 * chromaW + x0 - 4);
                    LoopFilter.SubblockFilterHorizontal(hevThreshold, interiorLimit, subBedgeLimit,
                        _frame.VBuffer, y0 * chromaW + x0 - 4);
                }
            }
        }

        // Filter across top of macroblock
        if (mby > 0)
        {
            if (_frame.FilterType)
            {
                for (int x = 0; x < 16; x++)
                {
                    int y0 = mby * 16;
                    int x0 = mbx * 16 + x;
                    LoopFilter.SimpleSegmentVertical(mbedgeLimit, _frame.YBuffer, y0 * lumaW + x0, lumaW);
                }
            }
            else
            {
                for (int x = 0; x < 16; x++)
                {
                    int y0 = mby * 16;
                    int x0 = mbx * 16 + x;
                    LoopFilter.MacroblockFilterVertical(hevThreshold, interiorLimit, mbedgeLimit,
                        _frame.YBuffer, y0 * lumaW + x0, lumaW);
                }

                for (int x = 0; x < 8; x++)
                {
                    int y0 = mby * 8;
                    int x0 = mbx * 8 + x;
                    LoopFilter.MacroblockFilterVertical(hevThreshold, interiorLimit, mbedgeLimit,
                        _frame.UBuffer, y0 * chromaW + x0, chromaW);
                    LoopFilter.MacroblockFilterVertical(hevThreshold, interiorLimit, mbedgeLimit,
                        _frame.VBuffer, y0 * chromaW + x0, chromaW);
                }
            }
        }

        // Filter across horizontal subblock edges
        if (doSubblockFiltering)
        {
            if (_frame.FilterType)
            {
                for (int y = 4; y < 15; y += 4)
                {
                    for (int x = 0; x < 16; x++)
                    {
                        int y0 = mby * 16 + y;
                        int x0 = mbx * 16 + x;
                        LoopFilter.SimpleSegmentVertical(subBedgeLimit, _frame.YBuffer, y0 * lumaW + x0, lumaW);
                    }
                }
            }
            else
            {
                for (int y = 4; y < 13; y += 4)
                {
                    for (int x = 0; x < 16; x++)
                    {
                        int y0 = mby * 16 + y;
                        int x0 = mbx * 16 + x;
                        LoopFilter.SubblockFilterVertical(hevThreshold, interiorLimit, subBedgeLimit,
                            _frame.YBuffer, y0 * lumaW + x0, lumaW);
                    }
                }

                for (int x = 0; x < 8; x++)
                {
                    int y0 = mby * 8 + 4;
                    int x0 = mbx * 8 + x;
                    LoopFilter.SubblockFilterVertical(hevThreshold, interiorLimit, subBedgeLimit,
                        _frame.UBuffer, y0 * chromaW + x0, chromaW);
                    LoopFilter.SubblockFilterVertical(hevThreshold, interiorLimit, subBedgeLimit,
                        _frame.VBuffer, y0 * chromaW + x0, chromaW);
                }
            }
        }
    }

    private (byte filterLevel, byte interiorLimit, byte hevThreshold) CalculateFilterParameters(ref MacroBlock mb)
    {
        var segment = _segments[mb.SegmentId];
        int filterLevel = _frame.FilterLevel;

        if (filterLevel == 0)
            return (0, 0, 0);

        if (_segmentsEnabled)
        {
            if (segment.DeltaValues)
                filterLevel += segment.LoopFilterLevel;
            else
                filterLevel = segment.LoopFilterLevel;
        }

        filterLevel = Math.Max(0, Math.Min(63, filterLevel));

        if (_loopFilterAdjustmentsEnabled)
        {
            filterLevel += _refDelta[0];
            if (mb.LumaMode == LumaMode.B)
                filterLevel += _modeDelta[0];
        }

        filterLevel = Math.Max(0, Math.Min(63, filterLevel));
        byte fl = (byte)filterLevel;

        // Interior limit
        byte interiorLimit = fl;
        if (_frame.SharpnessLevel > 0)
        {
            interiorLimit >>= _frame.SharpnessLevel > 4 ? 2 : 1;
            if (interiorLimit > 9 - _frame.SharpnessLevel)
                interiorLimit = (byte)(9 - _frame.SharpnessLevel);
        }

        if (interiorLimit == 0)
            interiorLimit = 1;

        // HEV threshold
        byte hevThreshold = fl >= 40 ? (byte)2 : (fl >= 15 ? (byte)1 : (byte)0);

        return (fl, interiorLimit, hevThreshold);
    }
}

/// <summary>
/// Macroblock data structure for VP8 decoding.
/// </summary>
internal struct MacroBlock
{
    public IntraMode[] BPred;
    public LumaMode LumaMode;
    public ChromaMode ChromaMode;
    public byte SegmentId;
    public bool CoeffsSkipped;
    public bool NonZeroDct;

    public MacroBlock()
    {
        BPred = new IntraMode[16];
        LumaMode = LumaMode.DC;
        ChromaMode = ChromaMode.DC;
        SegmentId = 0;
        CoeffsSkipped = false;
        NonZeroDct = false;
    }
}

/// <summary>
/// Previous macroblock info for prediction.
/// </summary>
internal struct PreviousMacroBlock
{
    public IntraMode[] BPred;
    public byte[] Complexity;

    public PreviousMacroBlock()
    {
        BPred = new IntraMode[4];
        Complexity = new byte[9];
    }
}
