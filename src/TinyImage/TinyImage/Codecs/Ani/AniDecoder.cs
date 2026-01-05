using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TinyImage.Codecs.Ico;

namespace TinyImage.Codecs.Ani;

/// <summary>
/// Decodes ANI (animated cursor) files.
/// </summary>
internal sealed class AniDecoder
{
    // RIFF chunk identifiers
    private static readonly uint RiffId = MakeFourCC("RIFF");
    private static readonly uint AconId = MakeFourCC("ACON");
    private static readonly uint ListId = MakeFourCC("LIST");
    private static readonly uint InfoId = MakeFourCC("INFO");
    private static readonly uint FramId = MakeFourCC("fram");
    private static readonly uint AnihId = MakeFourCC("anih");
    private static readonly uint RateId = MakeFourCC("rate");
    private static readonly uint SeqId = MakeFourCC("seq ");
    private static readonly uint IconId = MakeFourCC("icon");
    private static readonly uint InamId = MakeFourCC("INAM");
    private static readonly uint IartId = MakeFourCC("IART");

    private readonly Stream _stream;
    private readonly byte[] _buffer = new byte[8];

    public AniDecoder(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    /// <summary>
    /// Decodes the ANI file.
    /// </summary>
    public (AniHeader header, AniMetadata metadata, List<byte[]> frameData) Decode()
    {
        _stream.Position = 0;

        // Read RIFF header
        uint riffId = ReadUInt32();
        if (riffId != RiffId)
            throw new InvalidOperationException("Not a valid RIFF file.");

        uint fileSize = ReadUInt32();
        uint formType = ReadUInt32();

        if (formType != AconId)
            throw new InvalidOperationException("Not a valid ANI file (expected ACON).");

        var header = new AniHeader();
        var metadata = new AniMetadata();
        var frameData = new List<byte[]>();
        List<uint>? rates = null;
        List<uint>? sequence = null;

        long endPos = _stream.Position + fileSize - 4; // -4 for formType already read

        // Parse chunks
        while (_stream.Position < endPos)
        {
            uint chunkId = ReadUInt32();
            uint chunkSize = ReadUInt32();
            long chunkDataEnd = _stream.Position + chunkSize;

            if (chunkId == ListId)
            {
                uint listType = ReadUInt32();

                if (listType == InfoId)
                {
                    // Parse INFO list
                    ParseInfoList(chunkDataEnd - 4, metadata);
                }
                else if (listType == FramId)
                {
                    // Parse frame list
                    ParseFrameList(chunkDataEnd - 4, frameData);
                }
                else
                {
                    // Skip unknown list
                    _stream.Position = chunkDataEnd;
                }
            }
            else if (chunkId == AnihId)
            {
                header = ReadAniHeader(chunkSize);
                metadata.DefaultDisplayRate = header.DisplayRate;
            }
            else if (chunkId == RateId)
            {
                rates = ReadRateChunk(chunkSize);
            }
            else if (chunkId == SeqId)
            {
                sequence = ReadSequenceChunk(chunkSize);
            }
            else
            {
                // Skip unknown chunk
                _stream.Position = chunkDataEnd;
            }

            // Align to word boundary
            if ((chunkSize & 1) == 1 && _stream.Position < endPos)
                _stream.Position++;
        }

        metadata.Rates = rates;
        metadata.Sequence = sequence;

        return (header, metadata, frameData);
    }

    private void ParseInfoList(long endPos, AniMetadata metadata)
    {
        while (_stream.Position < endPos)
        {
            uint chunkId = ReadUInt32();
            uint chunkSize = ReadUInt32();
            long chunkDataEnd = _stream.Position + chunkSize;

            if (chunkId == InamId)
            {
                metadata.Title = ReadZString((int)chunkSize);
            }
            else if (chunkId == IartId)
            {
                metadata.Author = ReadZString((int)chunkSize);
            }
            else
            {
                _stream.Position = chunkDataEnd;
            }

            // Align to word boundary
            if ((chunkSize & 1) == 1 && _stream.Position < endPos)
                _stream.Position++;
        }
    }

    private void ParseFrameList(long endPos, List<byte[]> frameData)
    {
        while (_stream.Position < endPos)
        {
            uint chunkId = ReadUInt32();
            uint chunkSize = ReadUInt32();
            long chunkDataEnd = _stream.Position + chunkSize;

            if (chunkId == IconId)
            {
                // Read the icon/cursor data
                var data = new byte[chunkSize];
                ReadExact(data, 0, (int)chunkSize);
                frameData.Add(data);
            }
            else
            {
                _stream.Position = chunkDataEnd;
            }

            // Align to word boundary
            if ((chunkSize & 1) == 1 && _stream.Position < endPos)
                _stream.Position++;
        }
    }

    private AniHeader ReadAniHeader(uint size)
    {
        if (size < AniHeader.ExpectedSize)
            throw new InvalidOperationException($"ANI header too small (expected {AniHeader.ExpectedSize}, got {size}).");

        var header = new AniHeader
        {
            HeaderSize = ReadUInt32(),
            NumFrames = ReadUInt32(),
            NumSteps = ReadUInt32(),
            Width = ReadUInt32(),
            Height = ReadUInt32(),
            BitCount = ReadUInt32(),
            NumPlanes = ReadUInt32(),
            DisplayRate = ReadUInt32(),
            Flags = (AniFlags)ReadUInt32()
        };

        // Skip any extra bytes
        if (size > AniHeader.ExpectedSize)
            _stream.Position += size - AniHeader.ExpectedSize;

        return header;
    }

    private List<uint> ReadRateChunk(uint size)
    {
        int count = (int)(size / 4);
        var rates = new List<uint>(count);

        for (int i = 0; i < count; i++)
            rates.Add(ReadUInt32());

        return rates;
    }

    private List<uint> ReadSequenceChunk(uint size)
    {
        int count = (int)(size / 4);
        var sequence = new List<uint>(count);

        for (int i = 0; i < count; i++)
            sequence.Add(ReadUInt32());

        return sequence;
    }

    private string ReadZString(int maxLength)
    {
        var bytes = new byte[maxLength];
        ReadExact(bytes, 0, maxLength);

        // Find null terminator
        int length = Array.IndexOf(bytes, (byte)0);
        if (length < 0) length = maxLength;

        return Encoding.ASCII.GetString(bytes, 0, length);
    }

    private uint ReadUInt32()
    {
        ReadExact(_buffer, 0, 4);
        return BinaryPrimitives.ReadUInt32LittleEndian(_buffer);
    }

    private void ReadExact(byte[] buffer, int offset, int count)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = _stream.Read(buffer, offset + totalRead, count - totalRead);
            if (read == 0)
                throw new EndOfStreamException("Unexpected end of stream.");
            totalRead += read;
        }
    }

    private static uint MakeFourCC(string s)
    {
        return (uint)(s[0] | (s[1] << 8) | (s[2] << 16) | (s[3] << 24));
    }
}
