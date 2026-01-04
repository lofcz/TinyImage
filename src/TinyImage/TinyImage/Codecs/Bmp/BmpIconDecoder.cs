using System;
using System.Buffers.Binary;
using System.IO;

namespace TinyImage.Codecs.Bmp;

/// <summary>
/// Decodes OS/2 Icon (IC), Pointer (PT), Color Icon (CI), and Color Pointer (CP) formats.
/// These formats contain AND/XOR monochrome masks for transparency information.
/// </summary>
internal sealed class BmpIconDecoder
{
    private readonly Stream _stream;
    private readonly ushort _imageType;
    
    private byte[]? _andMask;  // Transparency mask (1 = transparent)
    private byte[]? _xorMask;  // XOR mask for monochrome icons
    private int _maskWidth;
    private int _maskHeight;
    
    /// <summary>
    /// Image types supported.
    /// </summary>
    public enum IconType
    {
        /// <summary>OS/2 monochrome icon.</summary>
        Icon = 0x4349,      // 'IC'
        /// <summary>OS/2 monochrome pointer.</summary>
        Pointer = 0x5450,   // 'PT'
        /// <summary>OS/2 color icon.</summary>
        ColorIcon = 0x4943, // 'CI'
        /// <summary>OS/2 color pointer.</summary>
        ColorPointer = 0x5043 // 'CP'
    }
    
    public BmpIconDecoder(Stream stream, ushort imageType)
    {
        _stream = stream;
        _imageType = imageType;
    }
    
    /// <summary>
    /// Gets whether this is a monochrome icon/pointer.
    /// </summary>
    public bool IsMonochrome => _imageType == (ushort)IconType.Icon || _imageType == (ushort)IconType.Pointer;
    
    /// <summary>
    /// Reads the AND/XOR masks from the stream.
    /// For color icons/pointers, positions the stream to read the color image.
    /// </summary>
    /// <returns>True if successful.</returns>
    public bool LoadMasks()
    {
        try
        {
            // Read the monochrome header first
            // For IC/PT: Single monochrome image with AND and XOR masks stacked
            // For CI/CP: Monochrome masks followed by a complete color image
            
            // Go back to read from file header
            long startPos = _stream.Position - 14; // We've already read the file header
            _stream.Seek(startPos, SeekOrigin.Begin);
            
            // Read the monochrome image using a separate decoder
            var monoDecoder = new BmpDecoder(_stream);
            var (width, height, pixels, _) = monoDecoder.Decode();
            
            // Monochrome icon has AND and XOR masks stacked vertically
            // Top half is XOR, bottom half is AND
            if (height % 2 != 0)
                return false;
                
            _maskWidth = width;
            _maskHeight = height / 2;
            
            // Extract AND mask (transparency) - bottom half, inverted (1 = opaque in our system)
            _andMask = new byte[_maskWidth * _maskHeight];
            for (int y = 0; y < _maskHeight; y++)
            {
                for (int x = 0; x < _maskWidth; x++)
                {
                    int srcOffset = ((y + _maskHeight) * width + x) * 4; // Bottom half
                    // AND mask: 0 = opaque, 1 = transparent
                    // Convert: original pixel value (either 0 or 255)
                    // We want: 255 = opaque, 0 = transparent
                    _andMask[(_maskHeight - 1 - y) * _maskWidth + x] = (byte)(255 - pixels[srcOffset]);
                }
            }
            
            // Extract XOR mask (color for monochrome) - top half
            _xorMask = new byte[_maskWidth * _maskHeight];
            for (int y = 0; y < _maskHeight; y++)
            {
                for (int x = 0; x < _maskWidth; x++)
                {
                    int srcOffset = (y * width + x) * 4; // Top half
                    _xorMask[(_maskHeight - 1 - y) * _maskWidth + x] = pixels[srcOffset];
                }
            }
            
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Gets the position in the stream where the color image data begins.
    /// Only valid for Color Icons/Pointers after LoadMasks().
    /// </summary>
    public long ColorImagePosition => _stream.Position;
    
    /// <summary>
    /// Applies the AND mask as alpha channel to the decoded color image.
    /// </summary>
    /// <param name="pixels">RGBA pixel data (4 bytes per pixel).</param>
    /// <param name="width">Image width.</param>
    /// <param name="height">Image height.</param>
    public void ApplyAlphaMask(byte[] pixels, int width, int height)
    {
        if (_andMask == null || width != _maskWidth || height != _maskHeight)
            return;
            
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int pixelOffset = (y * width + x) * 4;
                int maskOffset = y * width + x;
                
                // Apply AND mask as alpha
                pixels[pixelOffset + 3] = _andMask[maskOffset];
            }
        }
    }
    
    /// <summary>
    /// Creates monochrome image from XOR mask with AND mask alpha.
    /// Used for IC/PT types that don't have separate color data.
    /// </summary>
    /// <returns>RGBA pixel data for the monochrome icon.</returns>
    public byte[] CreateMonochromeImage()
    {
        if (_xorMask == null || _andMask == null)
            throw new InvalidOperationException("Masks not loaded.");
            
        var pixels = new byte[_maskWidth * _maskHeight * 4];
        
        for (int y = 0; y < _maskHeight; y++)
        {
            for (int x = 0; x < _maskWidth; x++)
            {
                int pixelOffset = (y * _maskWidth + x) * 4;
                int maskOffset = y * _maskWidth + x;
                
                byte gray = _xorMask[maskOffset];
                pixels[pixelOffset] = gray;     // R
                pixels[pixelOffset + 1] = gray; // G
                pixels[pixelOffset + 2] = gray; // B
                pixels[pixelOffset + 3] = _andMask[maskOffset]; // A
            }
        }
        
        return pixels;
    }
    
    /// <summary>
    /// Gets the mask dimensions.
    /// </summary>
    public (int width, int height) MaskDimensions => (_maskWidth, _maskHeight);
}
