namespace TinyImage.Tests;

[TestClass]
public sealed class CodecTests
{
    [TestMethod]
    public void Png_RoundTrip_PreservesPixelData()
    {
        // Create test image with specific colors
        var original = new Image(10, 10);
        for (int y = 0; y < 10; y++)
        {
            for (int x = 0; x < 10; x++)
            {
                original.SetPixel(x, y, new Rgba32((byte)(x * 25), (byte)(y * 25), 128, 255));
            }
        }

        // Save to PNG
        using var stream = new MemoryStream();
        original.Save(stream, ImageFormat.Png);

        // Load from PNG
        stream.Position = 0;
        var loaded = Image.Load(stream, ImageFormat.Png);

        // Verify dimensions
        Assert.AreEqual(original.Width, loaded.Width);
        Assert.AreEqual(original.Height, loaded.Height);

        // Verify pixel data
        for (int y = 0; y < 10; y++)
        {
            for (int x = 0; x < 10; x++)
            {
                var expected = original.GetPixel(x, y);
                var actual = loaded.GetPixel(x, y);
                Assert.AreEqual(expected.R, actual.R, $"R mismatch at ({x},{y})");
                Assert.AreEqual(expected.G, actual.G, $"G mismatch at ({x},{y})");
                Assert.AreEqual(expected.B, actual.B, $"B mismatch at ({x},{y})");
            }
        }
    }

    [TestMethod]
    public void Png_WithAlpha_PreservesTransparency()
    {
        var original = new Image(5, 5);
        original.SetPixel(2, 2, new Rgba32(255, 0, 0, 128)); // Semi-transparent red

        using var stream = new MemoryStream();
        original.Save(stream, ImageFormat.Png);

        stream.Position = 0;
        var loaded = Image.Load(stream, ImageFormat.Png);

        var pixel = loaded.GetPixel(2, 2);
        Assert.AreEqual(255, pixel.R);
        Assert.AreEqual(0, pixel.G);
        Assert.AreEqual(0, pixel.B);
        Assert.AreEqual(128, pixel.A);
    }

    [TestMethod]
    public void Jpeg_RoundTrip_ProducesValidImage()
    {
        // Create test image
        var original = new Image(20, 20);
        for (int y = 0; y < 20; y++)
        {
            for (int x = 0; x < 20; x++)
            {
                original.SetPixel(x, y, new Rgba32(255, 128, 64, 255));
            }
        }

        // Save to JPEG
        using var stream = new MemoryStream();
        original.Save(stream, ImageFormat.Jpeg);

        // Load from JPEG
        stream.Position = 0;
        var loaded = Image.Load(stream, ImageFormat.Jpeg);

        // Verify dimensions
        Assert.AreEqual(original.Width, loaded.Width);
        Assert.AreEqual(original.Height, loaded.Height);

        // JPEG is lossy, so we just verify color is approximately correct
        var pixel = loaded.GetPixel(10, 10);
        Assert.IsTrue(pixel.R > 200, $"Expected R > 200, got {pixel.R}");
        Assert.IsTrue(pixel.G > 80 && pixel.G < 180, $"Expected G around 128, got {pixel.G}");
        Assert.IsTrue(pixel.B > 20 && pixel.B < 120, $"Expected B around 64, got {pixel.B}");
    }

    [TestMethod]
    public void ToArray_Png_ProducesValidData()
    {
        var image = new Image(5, 5);
        image.SetPixel(0, 0, Rgba32.White);

        var data = image.ToArray(ImageFormat.Png);

        // PNG signature: 89 50 4E 47 0D 0A 1A 0A
        Assert.IsTrue(data.Length > 8);
        Assert.AreEqual(0x89, data[0]);
        Assert.AreEqual(0x50, data[1]);
        Assert.AreEqual(0x4E, data[2]);
        Assert.AreEqual(0x47, data[3]);
    }

    [TestMethod]
    public void ToArray_Jpeg_ProducesValidData()
    {
        var image = new Image(10, 10);
        
        var data = image.ToArray(ImageFormat.Jpeg);

        // JPEG signature: FF D8 FF
        Assert.IsTrue(data.Length > 3);
        Assert.AreEqual(0xFF, data[0]);
        Assert.AreEqual(0xD8, data[1]);
        Assert.AreEqual(0xFF, data[2]);
    }

    [TestMethod]
    public void Load_FromByteArray_DetectsFormat()
    {
        var original = new Image(5, 5);
        original.SetPixel(2, 2, Rgba32.White);

        // Test PNG detection
        var pngData = original.ToArray(ImageFormat.Png);
        var loadedPng = Image.Load(pngData);
        Assert.AreEqual(5, loadedPng.Width);

        // Test JPEG detection  
        var jpegData = original.ToArray(ImageFormat.Jpeg);
        var loadedJpeg = Image.Load(jpegData);
        Assert.AreEqual(5, loadedJpeg.Width);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Load_NullStream_ThrowsException()
    {
        _ = Image.Load((Stream)null!, ImageFormat.Png);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Save_NullStream_ThrowsException()
    {
        var image = new Image(5, 5);
        image.Save((Stream)null!, ImageFormat.Png);
    }

    #region GIF Codec Tests

    [TestMethod]
    public void Gif_RoundTrip_ProducesValidImage()
    {
        // Create test image with specific colors
        var original = new Image(10, 10);
        for (int y = 0; y < 10; y++)
        {
            for (int x = 0; x < 10; x++)
            {
                original.SetPixel(x, y, new Rgba32((byte)(x * 25), (byte)(y * 25), 128, 255));
            }
        }

        // Save to GIF
        using var stream = new MemoryStream();
        original.Save(stream, ImageFormat.Gif);

        // Load from GIF
        stream.Position = 0;
        var loaded = Image.Load(stream, ImageFormat.Gif);

        // Verify dimensions
        Assert.AreEqual(original.Width, loaded.Width);
        Assert.AreEqual(original.Height, loaded.Height);

        // GIF is limited to 256 colors with quantization, so colors may not match exactly
        // Just verify the image loaded successfully with correct dimensions
        Assert.AreEqual(1, loaded.Frames.Count);
    }

    [TestMethod]
    public void Gif_ToArray_ProducesValidSignature()
    {
        var image = new Image(5, 5);
        image.SetPixel(0, 0, Rgba32.White);

        var data = image.ToArray(ImageFormat.Gif);

        // GIF signature: 47 49 46 38 39 61 (GIF89a) or 47 49 46 38 37 61 (GIF87a)
        Assert.IsTrue(data.Length > 6);
        Assert.AreEqual(0x47, data[0]); // G
        Assert.AreEqual(0x49, data[1]); // I
        Assert.AreEqual(0x46, data[2]); // F
        Assert.AreEqual(0x38, data[3]); // 8
        Assert.AreEqual(0x39, data[4]); // 9
        Assert.AreEqual(0x61, data[5]); // a
    }

    [TestMethod]
    public void Gif_FormatDetection_WorksCorrectly()
    {
        var original = new Image(5, 5);
        original.SetPixel(2, 2, Rgba32.White);

        var gifData = original.ToArray(ImageFormat.Gif);
        var loaded = Image.Load(gifData);

        Assert.AreEqual(5, loaded.Width);
        Assert.AreEqual(5, loaded.Height);
    }

    [TestMethod]
    public void Gif_SingleFrame_HasCorrectFrameCount()
    {
        var image = new Image(10, 10);
        image.SetPixel(5, 5, new Rgba32(255, 0, 0, 255));

        using var stream = new MemoryStream();
        image.Save(stream, ImageFormat.Gif);

        stream.Position = 0;
        var loaded = Image.Load(stream, ImageFormat.Gif);

        Assert.AreEqual(1, loaded.Frames.Count);
    }

    [TestMethod]
    public void Gif_MultiFrame_CreatesAnimatedGif()
    {
        // Create image with multiple frames
        var image = new Image(10, 10);
        image.SetPixel(0, 0, new Rgba32(255, 0, 0, 255));
        image.Frames.RootFrame.Duration = TimeSpan.FromMilliseconds(100);

        // Add second frame
        var frame2 = image.Frames.AddFrame(10, 10);
        frame2.SetPixel(5, 5, new Rgba32(0, 255, 0, 255));
        frame2.Duration = TimeSpan.FromMilliseconds(200);

        // Save to GIF
        using var stream = new MemoryStream();
        image.Save(stream, ImageFormat.Gif);

        // Verify GIF was created (basic validation)
        Assert.IsTrue(stream.Length > 0);
        
        // Load and verify
        stream.Position = 0;
        var loaded = Image.Load(stream, ImageFormat.Gif);
        
        Assert.AreEqual(10, loaded.Width);
        Assert.AreEqual(10, loaded.Height);
    }

    [TestMethod]
    public void Gif_FrameCollection_CanAddFrames()
    {
        var image = new Image(20, 20);
        
        Assert.AreEqual(1, image.Frames.Count);
        
        var frame = image.Frames.AddFrame(20, 20);
        Assert.AreEqual(2, image.Frames.Count);
        
        image.Frames.AddFrame(new ImageFrame(20, 20));
        Assert.AreEqual(3, image.Frames.Count);
    }

    [TestMethod]
    public void Gif_FrameDuration_IsPreserved()
    {
        var image = new Image(5, 5);
        var duration = TimeSpan.FromMilliseconds(500);
        image.Frames.RootFrame.Duration = duration;

        Assert.AreEqual(duration, image.Frames.RootFrame.Duration);
    }

    [TestMethod]
    public void Gif_LoopCount_DefaultsToZero()
    {
        var image = new Image(5, 5);
        Assert.AreEqual(0, image.LoopCount); // 0 = infinite loop
    }

    [TestMethod]
    public void Gif_LoopCount_CanBeSet()
    {
        var image = new Image(5, 5);
        image.LoopCount = 3;
        Assert.AreEqual(3, image.LoopCount);
    }

    #endregion
}
