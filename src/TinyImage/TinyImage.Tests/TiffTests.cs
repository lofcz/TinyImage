namespace TinyImage.Tests;

[TestClass]
public sealed class TiffTests
{
    #region Basic Round-Trip Tests

    [TestMethod]
    public void Tiff_RoundTrip_NoCompression_PreservesPixelData()
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

        // Save to TIFF
        using var stream = new MemoryStream();
        original.Save(stream, ImageFormat.Tiff);

        // Load from TIFF
        stream.Position = 0;
        var loaded = Image.Load(stream, ImageFormat.Tiff);

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
    public void Tiff_WithAlpha_PreservesTransparency()
    {
        var original = new Image(5, 5, hasAlpha: true);
        original.SetPixel(2, 2, new Rgba32(255, 0, 0, 128)); // Semi-transparent red

        using var stream = new MemoryStream();
        original.Save(stream, ImageFormat.Tiff);

        stream.Position = 0;
        var loaded = Image.Load(stream, ImageFormat.Tiff);

        var pixel = loaded.GetPixel(2, 2);
        Assert.AreEqual(255, pixel.R);
        Assert.AreEqual(0, pixel.G);
        Assert.AreEqual(0, pixel.B);
        Assert.AreEqual(128, pixel.A);
    }

    [TestMethod]
    public void Tiff_ToArray_ProducesValidSignature()
    {
        var image = new Image(5, 5);
        image.SetPixel(0, 0, Rgba32.White);

        var data = image.ToArray(ImageFormat.Tiff);

        // TIFF little-endian signature: 49 49 2A 00
        Assert.IsTrue(data.Length > 8);
        Assert.AreEqual(0x49, data[0]); // I
        Assert.AreEqual(0x49, data[1]); // I
        Assert.AreEqual(0x2A, data[2]); // 42 (magic)
        Assert.AreEqual(0x00, data[3]); // 0
    }

    [TestMethod]
    public void Tiff_FormatDetection_LittleEndian()
    {
        var original = new Image(5, 5);
        original.SetPixel(2, 2, Rgba32.White);

        var tiffData = original.ToArray(ImageFormat.Tiff);
        var loaded = Image.Load(tiffData);

        Assert.AreEqual(5, loaded.Width);
        Assert.AreEqual(5, loaded.Height);
    }

    #endregion

    #region Image Size Tests

    [TestMethod]
    public void Tiff_SmallImage_1x1()
    {
        var original = new Image(1, 1);
        original.SetPixel(0, 0, new Rgba32(100, 150, 200, 255));

        using var stream = new MemoryStream();
        original.Save(stream, ImageFormat.Tiff);

        stream.Position = 0;
        var loaded = Image.Load(stream, ImageFormat.Tiff);

        Assert.AreEqual(1, loaded.Width);
        Assert.AreEqual(1, loaded.Height);
        var pixel = loaded.GetPixel(0, 0);
        Assert.AreEqual(100, pixel.R);
        Assert.AreEqual(150, pixel.G);
        Assert.AreEqual(200, pixel.B);
    }

    [TestMethod]
    public void Tiff_LargerImage_100x100()
    {
        var original = new Image(100, 100);
        for (int y = 0; y < 100; y++)
        {
            for (int x = 0; x < 100; x++)
            {
                original.SetPixel(x, y, new Rgba32((byte)x, (byte)y, 128, 255));
            }
        }

        using var stream = new MemoryStream();
        original.Save(stream, ImageFormat.Tiff);

        stream.Position = 0;
        var loaded = Image.Load(stream, ImageFormat.Tiff);

        Assert.AreEqual(100, loaded.Width);
        Assert.AreEqual(100, loaded.Height);

        // Sample some pixels
        Assert.AreEqual(50, loaded.GetPixel(50, 50).R);
        Assert.AreEqual(50, loaded.GetPixel(50, 50).G);
        Assert.AreEqual(128, loaded.GetPixel(50, 50).B);
    }

    [TestMethod]
    public void Tiff_NonSquareImage()
    {
        var original = new Image(200, 50);
        original.SetPixel(100, 25, new Rgba32(255, 0, 255, 255));

        using var stream = new MemoryStream();
        original.Save(stream, ImageFormat.Tiff);

        stream.Position = 0;
        var loaded = Image.Load(stream, ImageFormat.Tiff);

        Assert.AreEqual(200, loaded.Width);
        Assert.AreEqual(50, loaded.Height);
    }

    #endregion

    #region Color Tests

    [TestMethod]
    public void Tiff_RgbColors_PreservedCorrectly()
    {
        var original = new Image(4, 1);
        original.SetPixel(0, 0, new Rgba32(255, 0, 0, 255));   // Red
        original.SetPixel(1, 0, new Rgba32(0, 255, 0, 255));   // Green
        original.SetPixel(2, 0, new Rgba32(0, 0, 255, 255));   // Blue
        original.SetPixel(3, 0, new Rgba32(255, 255, 255, 255)); // White

        using var stream = new MemoryStream();
        original.Save(stream, ImageFormat.Tiff);

        stream.Position = 0;
        var loaded = Image.Load(stream, ImageFormat.Tiff);

        var red = loaded.GetPixel(0, 0);
        Assert.AreEqual(255, red.R);
        Assert.AreEqual(0, red.G);
        Assert.AreEqual(0, red.B);

        var green = loaded.GetPixel(1, 0);
        Assert.AreEqual(0, green.R);
        Assert.AreEqual(255, green.G);
        Assert.AreEqual(0, green.B);

        var blue = loaded.GetPixel(2, 0);
        Assert.AreEqual(0, blue.R);
        Assert.AreEqual(0, blue.G);
        Assert.AreEqual(255, blue.B);

        var white = loaded.GetPixel(3, 0);
        Assert.AreEqual(255, white.R);
        Assert.AreEqual(255, white.G);
        Assert.AreEqual(255, white.B);
    }

    [TestMethod]
    public void Tiff_GrayscaleValues_PreservedCorrectly()
    {
        var original = new Image(5, 1);
        for (int x = 0; x < 5; x++)
        {
            byte gray = (byte)(x * 50);
            original.SetPixel(x, 0, new Rgba32(gray, gray, gray, 255));
        }

        using var stream = new MemoryStream();
        original.Save(stream, ImageFormat.Tiff);

        stream.Position = 0;
        var loaded = Image.Load(stream, ImageFormat.Tiff);

        for (int x = 0; x < 5; x++)
        {
            byte expectedGray = (byte)(x * 50);
            var pixel = loaded.GetPixel(x, 0);
            Assert.AreEqual(expectedGray, pixel.R, $"Gray value mismatch at x={x}");
            Assert.AreEqual(expectedGray, pixel.G);
            Assert.AreEqual(expectedGray, pixel.B);
        }
    }

    #endregion

    #region Exception Tests

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Tiff_Load_NullStream_ThrowsException()
    {
        _ = Image.Load((Stream)null!, ImageFormat.Tiff);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Tiff_Save_NullStream_ThrowsException()
    {
        var image = new Image(5, 5);
        image.Save((Stream)null!, ImageFormat.Tiff);
    }

    #endregion

    #region Multi-Strip Tests

    [TestMethod]
    public void Tiff_MultiStrip_LargeImage()
    {
        // Create an image large enough to require multiple strips
        var original = new Image(50, 200);
        for (int y = 0; y < 200; y++)
        {
            for (int x = 0; x < 50; x++)
            {
                original.SetPixel(x, y, new Rgba32((byte)x, (byte)(y % 256), 128, 255));
            }
        }

        using var stream = new MemoryStream();
        original.Save(stream, ImageFormat.Tiff);

        stream.Position = 0;
        var loaded = Image.Load(stream, ImageFormat.Tiff);

        Assert.AreEqual(50, loaded.Width);
        Assert.AreEqual(200, loaded.Height);

        // Verify some pixels from different strips
        Assert.AreEqual(25, loaded.GetPixel(25, 10).R);
        Assert.AreEqual(25, loaded.GetPixel(25, 100).R);
        Assert.AreEqual(25, loaded.GetPixel(25, 190).R);
    }

    #endregion

    #region Gradient Test

    [TestMethod]
    public void Tiff_Gradient_PreservedCorrectly()
    {
        // Create a gradient image
        var original = new Image(256, 1);
        for (int x = 0; x < 256; x++)
        {
            original.SetPixel(x, 0, new Rgba32((byte)x, (byte)(255 - x), 128, 255));
        }

        using var stream = new MemoryStream();
        original.Save(stream, ImageFormat.Tiff);

        stream.Position = 0;
        var loaded = Image.Load(stream, ImageFormat.Tiff);

        Assert.AreEqual(256, loaded.Width);
        Assert.AreEqual(1, loaded.Height);

        // Verify gradient
        for (int x = 0; x < 256; x++)
        {
            var pixel = loaded.GetPixel(x, 0);
            Assert.AreEqual((byte)x, pixel.R, $"Red gradient mismatch at x={x}");
            Assert.AreEqual((byte)(255 - x), pixel.G, $"Green gradient mismatch at x={x}");
            Assert.AreEqual(128, pixel.B, $"Blue value mismatch at x={x}");
        }
    }

    #endregion
}
