using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TinyImage.Codecs.Ani;

/// <summary>
/// Encodes ANI (animated cursor) files.
/// </summary>
internal sealed class AniEncoder
{
    private readonly Stream _stream;

    public AniEncoder(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    /// <summary>
    /// Encodes frames into an ANI file.
    /// </summary>
    /// <param name="frameData">List of ICO/CUR data for each frame.</param>
    /// <param name="metadata">Animation metadata.</param>
    public void Encode(List<byte[]> frameData, AniMetadata metadata)
    {
        if (frameData == null || frameData.Count == 0)
            throw new ArgumentException("At least one frame is required.", nameof(frameData));

        metadata ??= new AniMetadata();

        uint numFrames = (uint)frameData.Count;
        uint numSteps = numFrames;
        bool hasSequence = metadata.Sequence != null && metadata.Sequence.Count > 0;
        bool hasRates = metadata.Rates != null && metadata.Rates.Count > 0;

        if (hasSequence)
            numSteps = (uint)metadata.Sequence!.Count;
        else if (hasRates)
            numSteps = (uint)metadata.Rates!.Count;

        // Build the file in memory to calculate sizes
        using var buffer = new MemoryStream();

        // Write ACON form type
        WriteFourCC(buffer, "ACON");

        // Optional INFO list
        byte[]? infoChunk = BuildInfoChunk(metadata);
        if (infoChunk != null)
            buffer.Write(infoChunk, 0, infoChunk.Length);

        // anih chunk
        WriteAnihChunk(buffer, numFrames, numSteps, metadata, hasSequence);

        // rate chunk (optional)
        if (hasRates)
            WriteRateChunk(buffer, metadata.Rates!, numSteps);

        // seq chunk (optional)
        if (hasSequence)
            WriteSeqChunk(buffer, metadata.Sequence!);

        // fram LIST
        WriteFrameList(buffer, frameData);

        // Now write the complete RIFF file
        long dataLength = buffer.Length;
        buffer.Position = 0;

        // RIFF header
        WriteFourCC(_stream, "RIFF");
        WriteUInt32(_stream, (uint)dataLength);

        // Copy content
        buffer.CopyTo(_stream);
    }

    private byte[]? BuildInfoChunk(AniMetadata metadata)
    {
        if (string.IsNullOrEmpty(metadata.Title) && string.IsNullOrEmpty(metadata.Author))
            return null;

        using var buffer = new MemoryStream();

        // LIST header placeholder (will update size)
        WriteFourCC(buffer, "LIST");
        long sizePos = buffer.Position;
        WriteUInt32(buffer, 0); // Placeholder

        WriteFourCC(buffer, "INFO");

        if (!string.IsNullOrEmpty(metadata.Title))
            WriteZStringChunk(buffer, "INAM", metadata.Title!);

        if (!string.IsNullOrEmpty(metadata.Author))
            WriteZStringChunk(buffer, "IART", metadata.Author!);

        // Update LIST size
        uint listSize = (uint)(buffer.Length - sizePos - 4);
        buffer.Position = sizePos;
        WriteUInt32(buffer, listSize);

        return buffer.ToArray();
    }

    private void WriteAnihChunk(Stream stream, uint numFrames, uint numSteps, AniMetadata metadata, bool hasSequence)
    {
        WriteFourCC(stream, "anih");
        WriteUInt32(stream, AniHeader.ExpectedSize);

        WriteUInt32(stream, AniHeader.ExpectedSize); // HeaderSize
        WriteUInt32(stream, numFrames);              // NumFrames
        WriteUInt32(stream, numSteps);               // NumSteps
        WriteUInt32(stream, 0);                      // Width (unused)
        WriteUInt32(stream, 0);                      // Height (unused)
        WriteUInt32(stream, 0);                      // BitCount (unused)
        WriteUInt32(stream, 1);                      // NumPlanes (unused, set to 1)
        WriteUInt32(stream, metadata.DefaultDisplayRate); // DisplayRate

        uint flags = (uint)AniFlags.Icon;
        if (hasSequence)
            flags |= (uint)AniFlags.Sequence;
        WriteUInt32(stream, flags);
    }

    private void WriteRateChunk(Stream stream, List<uint> rates, uint numSteps)
    {
        WriteFourCC(stream, "rate");
        WriteUInt32(stream, numSteps * 4);

        for (int i = 0; i < numSteps; i++)
        {
            uint rate = (i < rates.Count) ? rates[i] : rates[rates.Count - 1];
            WriteUInt32(stream, rate);
        }
    }

    private void WriteSeqChunk(Stream stream, List<uint> sequence)
    {
        WriteFourCC(stream, "seq ");
        WriteUInt32(stream, (uint)(sequence.Count * 4));

        foreach (uint idx in sequence)
            WriteUInt32(stream, idx);
    }

    private void WriteFrameList(Stream stream, List<byte[]> frameData)
    {
        // Calculate total size
        uint listDataSize = 4; // 'fram' identifier
        foreach (var data in frameData)
        {
            listDataSize += 8; // 'icon' + size
            listDataSize += (uint)data.Length;
            if ((data.Length & 1) == 1) listDataSize++; // Padding
        }

        WriteFourCC(stream, "LIST");
        WriteUInt32(stream, listDataSize);
        WriteFourCC(stream, "fram");

        foreach (var data in frameData)
        {
            WriteFourCC(stream, "icon");
            WriteUInt32(stream, (uint)data.Length);
            stream.Write(data, 0, data.Length);

            // Pad to word boundary
            if ((data.Length & 1) == 1)
                stream.WriteByte(0);
        }
    }

    private void WriteZStringChunk(Stream stream, string chunkId, string value)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(value + '\0');
        WriteFourCC(stream, chunkId);
        WriteUInt32(stream, (uint)bytes.Length);
        stream.Write(bytes, 0, bytes.Length);

        // Pad to word boundary
        if ((bytes.Length & 1) == 1)
            stream.WriteByte(0);
    }

    private static void WriteFourCC(Stream stream, string fourcc)
    {
        stream.WriteByte((byte)fourcc[0]);
        stream.WriteByte((byte)fourcc[1]);
        stream.WriteByte((byte)fourcc[2]);
        stream.WriteByte((byte)fourcc[3]);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        stream.WriteByte((byte)(value & 0xFF));
        stream.WriteByte((byte)((value >> 8) & 0xFF));
        stream.WriteByte((byte)((value >> 16) & 0xFF));
        stream.WriteByte((byte)((value >> 24) & 0xFF));
    }
}
