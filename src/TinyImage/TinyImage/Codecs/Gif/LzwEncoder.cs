using System;
using System.IO;

namespace TinyImage.Codecs.Gif;

/// <summary>
/// LZW encoder for GIF image data compression.
/// Adapted from AnimatedGifEncoder (MIT License).
/// </summary>
internal sealed class LzwEncoder
{
    private const int Eof = -1;
    private const int Bits = 12;
    private const int HashSize = 5003;
    private const int MaxBits = Bits;
    private const int MaxMaxCode = 1 << Bits;

    private readonly byte[] _pixels;
    private readonly int _initCodeSize;

    private int _currentPixel;
    private int _numBits;
    private int _maxCode;
    private int _freeEntry;
    private bool _clearFlag;
    private int _initBits;
    private int _clearCode;
    private int _eofCode;

    private int _currentAccum;
    private int _currentBits;

    private readonly int[] _hashTable = new int[HashSize];
    private readonly int[] _codeTable = new int[HashSize];

    private readonly int[] _masks =
    {
        0x0000, 0x0001, 0x0003, 0x0007, 0x000F, 0x001F, 0x003F, 0x007F,
        0x00FF, 0x01FF, 0x03FF, 0x07FF, 0x0FFF, 0x1FFF, 0x3FFF, 0x7FFF, 0xFFFF
    };

    private int _accumCount;
    private readonly byte[] _accumBuffer = new byte[256];

    /// <summary>
    /// Creates a new LZW encoder for the given indexed pixels.
    /// </summary>
    /// <param name="pixels">The indexed pixel data.</param>
    /// <param name="colorDepth">The color depth (bits per pixel, typically 8).</param>
    public LzwEncoder(byte[] pixels, int colorDepth)
    {
        _pixels = pixels ?? throw new ArgumentNullException(nameof(pixels));
        _initCodeSize = Math.Max(2, colorDepth);
    }

    /// <summary>
    /// Encodes the pixels and writes to the stream.
    /// </summary>
    public void Encode(Stream stream)
    {
        // Write initial code size
        stream.WriteByte((byte)_initCodeSize);

        _currentPixel = 0;

        Compress(_initCodeSize + 1, stream);

        // Write block terminator
        stream.WriteByte(0);
    }

    private void Compress(int initBits, Stream stream)
    {
        _initBits = initBits;
        _clearFlag = false;
        _numBits = _initBits;
        _maxCode = MaxCode(_numBits);

        _clearCode = 1 << (initBits - 1);
        _eofCode = _clearCode + 1;
        _freeEntry = _clearCode + 2;

        _accumCount = 0;
        _currentAccum = 0;
        _currentBits = 0;

        int ent = NextPixel();

        int hshift = 0;
        for (int fcode = HashSize; fcode < 65536; fcode *= 2)
        {
            hshift++;
        }
        hshift = 8 - hshift;

        ClearHashTable();
        Output(_clearCode, stream);

        int c;
        while ((c = NextPixel()) != Eof)
        {
            int fcode = (c << MaxBits) + ent;
            int i = (c << hshift) ^ ent;

            if (_hashTable[i] == fcode)
            {
                ent = _codeTable[i];
                continue;
            }

            if (_hashTable[i] >= 0)
            {
                int disp = HashSize - i;
                if (i == 0)
                    disp = 1;

                do
                {
                    i -= disp;
                    if (i < 0)
                        i += HashSize;

                    if (_hashTable[i] == fcode)
                    {
                        ent = _codeTable[i];
                        goto nextPixelLoop;
                    }
                } while (_hashTable[i] >= 0);
            }

            Output(ent, stream);
            ent = c;

            if (_freeEntry < MaxMaxCode)
            {
                _codeTable[i] = _freeEntry++;
                _hashTable[i] = fcode;
            }
            else
            {
                ClearBlock(stream);
            }

            nextPixelLoop:;
        }

        Output(ent, stream);
        Output(_eofCode, stream);
    }

    private void Output(int code, Stream stream)
    {
        _currentAccum &= _masks[_currentBits];

        if (_currentBits > 0)
            _currentAccum |= code << _currentBits;
        else
            _currentAccum = code;

        _currentBits += _numBits;

        while (_currentBits >= 8)
        {
            AddByte((byte)(_currentAccum & 0xFF), stream);
            _currentAccum >>= 8;
            _currentBits -= 8;
        }

        if (_freeEntry > _maxCode || _clearFlag)
        {
            if (_clearFlag)
            {
                _maxCode = MaxCode(_numBits = _initBits);
                _clearFlag = false;
            }
            else
            {
                _numBits++;
                _maxCode = _numBits == MaxBits ? MaxMaxCode : MaxCode(_numBits);
            }
        }

        if (code == _eofCode)
        {
            while (_currentBits > 0)
            {
                AddByte((byte)(_currentAccum & 0xFF), stream);
                _currentAccum >>= 8;
                _currentBits -= 8;
            }
            FlushBytes(stream);
        }
    }

    private void AddByte(byte b, Stream stream)
    {
        _accumBuffer[_accumCount++] = b;
        if (_accumCount >= 254)
            FlushBytes(stream);
    }

    private void FlushBytes(Stream stream)
    {
        if (_accumCount > 0)
        {
            stream.WriteByte((byte)_accumCount);
            stream.Write(_accumBuffer, 0, _accumCount);
            _accumCount = 0;
        }
    }

    private void ClearBlock(Stream stream)
    {
        ClearHashTable();
        _freeEntry = _clearCode + 2;
        _clearFlag = true;
        Output(_clearCode, stream);
    }

    private void ClearHashTable()
    {
        for (int i = 0; i < HashSize; i++)
        {
            _hashTable[i] = -1;
        }
    }

    private static int MaxCode(int nBits)
    {
        return (1 << nBits) - 1;
    }

    private int NextPixel()
    {
        if (_currentPixel < _pixels.Length)
        {
            return _pixels[_currentPixel++] & 0xFF;
        }
        return Eof;
    }
}
