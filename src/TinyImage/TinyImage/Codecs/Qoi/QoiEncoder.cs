using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace TinyImage.Codecs.Qoi;

/// <summary>
/// Encodes raw RGBA32 pixel data into QOI (Quite OK Image) format.
/// </summary>
/// <remarks>
/// Based on QoiSharp by Eugene Antonov (MIT License) and the QOI specification
/// by Dominic Szablewski.
/// </remarks>
internal sealed class QoiEncoder
{
    private readonly Stream _stream;
    private readonly int _width;
    private readonly int _height;
    private readonly byte[] _pixels;
    private readonly bool _hasAlpha;
    private readonly QoiColorSpace _colorSpace;

    /// <summary>
    /// Creates a new QOI encoder.
    /// </summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="pixels">RGBA32 pixel data (4 bytes per pixel).</param>
    /// <param name="hasAlpha">Whether the image has meaningful alpha values.</param>
    /// <param name="colorSpace">The color space to encode in the header.</param>
    public QoiEncoder(Stream stream, int width, int height, byte[] pixels, bool hasAlpha, QoiColorSpace colorSpace = QoiColorSpace.SRgb)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _width = width;
        _height = height;
        _pixels = pixels ?? throw new ArgumentNullException(nameof(pixels));
        _hasAlpha = hasAlpha;
        _colorSpace = colorSpace;

        ValidateInput();
    }

    private void ValidateInput()
    {
        if (_width <= 0)
        {
            throw new QoiEncodingException($"Invalid width: {_width}");
        }

        if (_height <= 0 || _height >= QoiConstants.MaxPixels / _width)
        {
            throw new QoiEncodingException($"Invalid height: {_height}. Maximum for this width is {QoiConstants.MaxPixels / _width - 1}");
        }

        long expectedLength = (long)_width * _height * 4;
        if (_pixels.Length != expectedLength)
        {
            throw new QoiEncodingException($"Invalid pixel data length: {_pixels.Length}. Expected: {expectedLength}");
        }
    }

    /// <summary>
    /// Encodes the image to the output stream.
    /// </summary>
    public void Encode()
    {
        // Calculate maximum output size (header + worst case pixels + padding)
        // Worst case: every pixel needs 5 bytes (QOI_OP_RGBA)
        long maxSize = QoiConstants.HeaderSize + ((long)_width * _height * 5) + QoiConstants.Padding.Length;
        
        // Use a reasonable buffer - for very large images we'll write in chunks
        int bufferSize = (int)Math.Min(maxSize, 1024 * 1024); // 1MB max buffer
        byte[] outputBuffer = new byte[bufferSize];
        int bufferPos = 0;

        // Write header
        WriteHeader(outputBuffer, ref bufferPos);

        // Encode pixels
        EncodePixels(outputBuffer, ref bufferPos);

        // Write padding (end marker)
        for (int i = 0; i < QoiConstants.Padding.Length; i++)
        {
            if (bufferPos >= outputBuffer.Length)
            {
                FlushBuffer(outputBuffer, ref bufferPos);
            }
            outputBuffer[bufferPos++] = QoiConstants.Padding[i];
        }

        // Flush remaining data
        if (bufferPos > 0)
        {
            _stream.Write(outputBuffer, 0, bufferPos);
        }
    }

    private void WriteHeader(byte[] buffer, ref int pos)
    {
        // Magic: "qoif"
        buffer[pos++] = (byte)'q';
        buffer[pos++] = (byte)'o';
        buffer[pos++] = (byte)'i';
        buffer[pos++] = (byte)'f';

        // Width (big-endian)
        buffer[pos++] = (byte)(_width >> 24);
        buffer[pos++] = (byte)(_width >> 16);
        buffer[pos++] = (byte)(_width >> 8);
        buffer[pos++] = (byte)_width;

        // Height (big-endian)
        buffer[pos++] = (byte)(_height >> 24);
        buffer[pos++] = (byte)(_height >> 16);
        buffer[pos++] = (byte)(_height >> 8);
        buffer[pos++] = (byte)_height;

        // Channels (4 for RGBA, 3 for RGB)
        buffer[pos++] = _hasAlpha ? (byte)4 : (byte)4; // Always write 4 channels since TinyImage uses RGBA32 internally

        // Color space
        buffer[pos++] = (byte)_colorSpace;
    }

    private void EncodePixels(byte[] outputBuffer, ref int bufferPos)
    {
        // Hash table for previously seen pixels
        int[] hashTable = new int[QoiConstants.HashTableSize];

        // Previous pixel state (start with r=0, g=0, b=0, a=255)
        byte prevR = 0, prevG = 0, prevB = 0, prevA = 255;
        int prevPixel = PackPixel(0, 0, 0, 255);

        int run = 0;
        int pixelCount = _width * _height;

        for (int i = 0; i < pixelCount; i++)
        {
            int srcOffset = i * 4;
            byte r = _pixels[srcOffset];
            byte g = _pixels[srcOffset + 1];
            byte b = _pixels[srcOffset + 2];
            byte a = _pixels[srcOffset + 3];
            int currentPixel = PackPixel(r, g, b, a);

            // Ensure buffer has space (worst case: 5 bytes per pixel + run byte)
            if (bufferPos + 6 >= outputBuffer.Length)
            {
                FlushBuffer(outputBuffer, ref bufferPos);
            }

            if (currentPixel == prevPixel)
            {
                // Same as previous - extend run
                run++;
                if (run == QoiConstants.MaxRunLength)
                {
                    // Max run length reached, write it out
                    outputBuffer[bufferPos++] = (byte)(QoiConstants.Run | (run - 1));
                    run = 0;
                }
            }
            else
            {
                // Different pixel - write any pending run first
                if (run > 0)
                {
                    outputBuffer[bufferPos++] = (byte)(QoiConstants.Run | (run - 1));
                    run = 0;
                }

                // Check hash table for matching pixel
                int hashIndex = QoiConstants.CalculateHashIndex(r, g, b, a);

                if (hashTable[hashIndex] == currentPixel)
                {
                    // QOI_OP_INDEX: pixel found in hash table
                    outputBuffer[bufferPos++] = (byte)(QoiConstants.Index | hashIndex);
                }
                else
                {
                    // Update hash table
                    hashTable[hashIndex] = currentPixel;

                    if (a == prevA)
                    {
                        // Alpha unchanged - try difference encodings
                        int dr = r - prevR;
                        int dg = g - prevG;
                        int db = b - prevB;

                        int drDg = dr - dg;
                        int dbDg = db - dg;

                        // Try QOI_OP_DIFF (-2..1 range for each channel)
                        if (dr >= -2 && dr <= 1 &&
                            dg >= -2 && dg <= 1 &&
                            db >= -2 && db <= 1)
                        {
                            outputBuffer[bufferPos++] = (byte)(QoiConstants.Diff |
                                ((dr + 2) << 4) |
                                ((dg + 2) << 2) |
                                (db + 2));
                        }
                        // Try QOI_OP_LUMA (green: -32..31, red/blue relative: -8..7)
                        else if (dg >= -32 && dg <= 31 &&
                                 drDg >= -8 && drDg <= 7 &&
                                 dbDg >= -8 && dbDg <= 7)
                        {
                            outputBuffer[bufferPos++] = (byte)(QoiConstants.Luma | (dg + 32));
                            outputBuffer[bufferPos++] = (byte)(((drDg + 8) << 4) | (dbDg + 8));
                        }
                        // QOI_OP_RGB: full RGB values
                        else
                        {
                            outputBuffer[bufferPos++] = QoiConstants.Rgb;
                            outputBuffer[bufferPos++] = r;
                            outputBuffer[bufferPos++] = g;
                            outputBuffer[bufferPos++] = b;
                        }
                    }
                    else
                    {
                        // Alpha changed - must use QOI_OP_RGBA
                        outputBuffer[bufferPos++] = QoiConstants.Rgba;
                        outputBuffer[bufferPos++] = r;
                        outputBuffer[bufferPos++] = g;
                        outputBuffer[bufferPos++] = b;
                        outputBuffer[bufferPos++] = a;
                    }
                }

                // Update previous pixel
                prevR = r;
                prevG = g;
                prevB = b;
                prevA = a;
                prevPixel = currentPixel;
            }
        }

        // Write any remaining run
        if (run > 0)
        {
            if (bufferPos >= outputBuffer.Length)
            {
                FlushBuffer(outputBuffer, ref bufferPos);
            }
            outputBuffer[bufferPos++] = (byte)(QoiConstants.Run | (run - 1));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int PackPixel(byte r, byte g, byte b, byte a)
    {
        return (r << 24) | (g << 16) | (b << 8) | a;
    }

    private void FlushBuffer(byte[] buffer, ref int pos)
    {
        if (pos > 0)
        {
            _stream.Write(buffer, 0, pos);
            pos = 0;
        }
    }
}
