using System;

namespace TinyImage.Codecs.Tiff;

/// <summary>
/// Provides color space conversion methods for TIFF images.
/// Based on UTIF.js toRGBA8 function and ITU-R BT.601 standard.
/// </summary>
internal static class TiffColorConverter
{
    /// <summary>
    /// Clamps a value between min and max (netstandard2.0 compatible).
    /// </summary>
    private static float Clamp(float value, float min, float max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    /// <summary>
    /// Clamps an integer value between min and max (netstandard2.0 compatible).
    /// </summary>
    private static int Clamp(int value, int min, int max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    /// <summary>
    /// Converts CMYK color to RGB.
    /// Uses the simple formula: RGB = (255 - CMY) * (1 - K/255)
    /// </summary>
    /// <param name="c">Cyan component (0-255)</param>
    /// <param name="m">Magenta component (0-255)</param>
    /// <param name="y">Yellow component (0-255)</param>
    /// <param name="k">Key/Black component (0-255)</param>
    /// <returns>RGB values as a tuple</returns>
    public static (byte R, byte G, byte B) CmykToRgb(byte c, byte m, byte y, byte k)
    {
        // Convert from CMYK to RGB using standard formula
        // RGB = (1 - C) * (1 - K)
        float kFactor = (255 - k) / 255f;
        
        byte r = (byte)Clamp((255 - c) * kFactor, 0, 255);
        byte g = (byte)Clamp((255 - m) * kFactor, 0, 255);
        byte b = (byte)Clamp((255 - y) * kFactor, 0, 255);
        
        return (r, g, b);
    }

    /// <summary>
    /// Converts CMYK color to RGB using 16-bit components.
    /// </summary>
    public static (byte R, byte G, byte B) CmykToRgb(ushort c, ushort m, ushort y, ushort k)
    {
        // Scale 16-bit to 8-bit and convert
        return CmykToRgb(
            (byte)(c >> 8),
            (byte)(m >> 8),
            (byte)(y >> 8),
            (byte)(k >> 8));
    }

    /// <summary>
    /// Converts YCbCr color to RGB using ITU-R BT.601 coefficients.
    /// </summary>
    /// <param name="y">Luminance component (0-255)</param>
    /// <param name="cb">Blue-difference chroma component (0-255)</param>
    /// <param name="cr">Red-difference chroma component (0-255)</param>
    /// <returns>RGB values as a tuple</returns>
    public static (byte R, byte G, byte B) YCbCrToRgb(byte y, byte cb, byte cr)
    {
        // ITU-R BT.601 conversion
        // R = Y + 1.402 * (Cr - 128)
        // G = Y - 0.344136 * (Cb - 128) - 0.714136 * (Cr - 128)
        // B = Y + 1.772 * (Cb - 128)
        
        int cbOffset = cb - 128;
        int crOffset = cr - 128;
        
        int r = y + (int)(1.402 * crOffset);
        int g = y - (int)(0.344136 * cbOffset) - (int)(0.714136 * crOffset);
        int b = y + (int)(1.772 * cbOffset);
        
        return (
            (byte)Clamp(r, 0, 255),
            (byte)Clamp(g, 0, 255),
            (byte)Clamp(b, 0, 255));
    }

    /// <summary>
    /// Converts YCbCr color to RGB using custom coefficients.
    /// </summary>
    /// <param name="y">Luminance component (0-255)</param>
    /// <param name="cb">Blue-difference chroma component (0-255)</param>
    /// <param name="cr">Red-difference chroma component (0-255)</param>
    /// <param name="lumaRed">Red luminance coefficient (default: 0.299)</param>
    /// <param name="lumaGreen">Green luminance coefficient (default: 0.587)</param>
    /// <param name="lumaBlue">Blue luminance coefficient (default: 0.114)</param>
    /// <returns>RGB values as a tuple</returns>
    public static (byte R, byte G, byte B) YCbCrToRgb(byte y, byte cb, byte cr, 
        float lumaRed, float lumaGreen, float lumaBlue)
    {
        // Using custom YCbCr coefficients
        // Standard formula with custom luma values
        float cbOffset = cb - 128f;
        float crOffset = cr - 128f;
        
        // Calculate conversion factors from luma coefficients
        float crToR = 2f * (1f - lumaRed);
        float cbToB = 2f * (1f - lumaBlue);
        float crToG = -2f * lumaRed * (1f - lumaRed) / lumaGreen;
        float cbToG = -2f * lumaBlue * (1f - lumaBlue) / lumaGreen;
        
        float r = y + crToR * crOffset;
        float g = y + crToG * crOffset + cbToG * cbOffset;
        float b = y + cbToB * cbOffset;
        
        return (
            (byte)Clamp(r, 0, 255),
            (byte)Clamp(g, 0, 255),
            (byte)Clamp(b, 0, 255));
    }

    /// <summary>
    /// Converts an entire CMYK buffer to RGB buffer.
    /// </summary>
    /// <param name="cmykData">Input CMYK data (4 bytes per pixel)</param>
    /// <param name="rgbData">Output RGB data (3 bytes per pixel)</param>
    /// <param name="pixelCount">Number of pixels to convert</param>
    public static void ConvertCmykBufferToRgb(byte[] cmykData, byte[] rgbData, int pixelCount)
    {
        for (int i = 0; i < pixelCount; i++)
        {
            int cmykIdx = i * 4;
            int rgbIdx = i * 3;
            
            if (cmykIdx + 3 >= cmykData.Length || rgbIdx + 2 >= rgbData.Length)
                break;
            
            var (r, g, b) = CmykToRgb(
                cmykData[cmykIdx],
                cmykData[cmykIdx + 1],
                cmykData[cmykIdx + 2],
                cmykData[cmykIdx + 3]);
            
            rgbData[rgbIdx] = r;
            rgbData[rgbIdx + 1] = g;
            rgbData[rgbIdx + 2] = b;
        }
    }

    /// <summary>
    /// Converts an entire YCbCr buffer to RGB buffer.
    /// </summary>
    /// <param name="ycbcrData">Input YCbCr data (3 bytes per pixel)</param>
    /// <param name="rgbData">Output RGB data (3 bytes per pixel)</param>
    /// <param name="pixelCount">Number of pixels to convert</param>
    public static void ConvertYCbCrBufferToRgb(byte[] ycbcrData, byte[] rgbData, int pixelCount)
    {
        for (int i = 0; i < pixelCount; i++)
        {
            int idx = i * 3;
            
            if (idx + 2 >= ycbcrData.Length || idx + 2 >= rgbData.Length)
                break;
            
            var (r, g, b) = YCbCrToRgb(
                ycbcrData[idx],
                ycbcrData[idx + 1],
                ycbcrData[idx + 2]);
            
            rgbData[idx] = r;
            rgbData[idx + 1] = g;
            rgbData[idx + 2] = b;
        }
    }

    /// <summary>
    /// Upsamples YCbCr data with subsampling (e.g., 4:2:2 or 4:2:0).
    /// Converts subsampled YCbCr to full resolution RGB.
    /// </summary>
    /// <param name="data">Input subsampled YCbCr data</param>
    /// <param name="width">Image width</param>
    /// <param name="height">Image height</param>
    /// <param name="horizSubsampling">Horizontal subsampling factor (1, 2, or 4)</param>
    /// <param name="vertSubsampling">Vertical subsampling factor (1, 2, or 4)</param>
    /// <returns>Upsampled RGB data</returns>
    public static byte[] UpsampleYCbCrToRgb(byte[] data, int width, int height, 
        int horizSubsampling, int vertSubsampling)
    {
        if (horizSubsampling == 1 && vertSubsampling == 1)
        {
            // No subsampling, just convert
            var result = new byte[width * height * 3];
            ConvertYCbCrBufferToRgb(data, result, width * height);
            return result;
        }

        var output = new byte[width * height * 3];
        
        // Calculate the number of MCU (Minimum Coded Unit) blocks
        int mcuWidth = horizSubsampling;
        int mcuHeight = vertSubsampling;
        int mcusAcross = (width + mcuWidth - 1) / mcuWidth;
        int mcusDown = (height + mcuHeight - 1) / mcuHeight;
        
        // Each MCU contains: horizSubsampling * vertSubsampling Y samples + 1 Cb + 1 Cr
        int samplesPerMcu = mcuWidth * mcuHeight + 2;
        int dataIndex = 0;
        
        for (int mcuY = 0; mcuY < mcusDown; mcuY++)
        {
            for (int mcuX = 0; mcuX < mcusAcross; mcuX++)
            {
                // Read Y samples for this MCU
                var yValues = new byte[mcuWidth * mcuHeight];
                for (int i = 0; i < mcuWidth * mcuHeight && dataIndex < data.Length; i++)
                {
                    yValues[i] = data[dataIndex++];
                }
                
                // Read Cb and Cr (shared for entire MCU)
                byte cb = dataIndex < data.Length ? data[dataIndex++] : (byte)128;
                byte cr = dataIndex < data.Length ? data[dataIndex++] : (byte)128;
                
                // Convert and place pixels
                for (int dy = 0; dy < mcuHeight; dy++)
                {
                    for (int dx = 0; dx < mcuWidth; dx++)
                    {
                        int px = mcuX * mcuWidth + dx;
                        int py = mcuY * mcuHeight + dy;
                        
                        if (px >= width || py >= height) continue;
                        
                        byte y = yValues[dy * mcuWidth + dx];
                        var (r, g, b) = YCbCrToRgb(y, cb, cr);
                        
                        int outIdx = (py * width + px) * 3;
                        output[outIdx] = r;
                        output[outIdx + 1] = g;
                        output[outIdx + 2] = b;
                    }
                }
            }
        }
        
        return output;
    }
}
