namespace TinyImage.Tests;

[TestClass]
public sealed class TgaTests
{
    #region Round-Trip Tests

    [TestMethod]
    public void Tga_RoundTrip_PreservesPixelData()
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

        // Save to TGA
        using var stream = new MemoryStream();
        original.Save(stream, ImageFormat.Tga);

        // Load from TGA
        stream.Position = 0;
        var loaded = Image.Load(stream, ImageFormat.Tga);

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
    public void Tga_RoundTrip_WithAlpha_PreservesTransparency()
    {
        var original = new Image(5, 5);
        original.SetPixel(2, 2, new Rgba32(255, 0, 0, 128)); // Semi-transparent red

        using var stream = new MemoryStream();
        original.Save(stream, ImageFormat.Tga);

        stream.Position = 0;
        var loaded = Image.Load(stream, ImageFormat.Tga);

        var pixel = loaded.GetPixel(2, 2);
        Assert.AreEqual(255, pixel.R);
        Assert.AreEqual(0, pixel.G);
        Assert.AreEqual(0, pixel.B);
        Assert.AreEqual(128, pixel.A);
    }

    [TestMethod]
    public void Tga_RoundTrip_NoAlpha_Works()
    {
        var original = new Image(10, 10, hasAlpha: false);
        for (int y = 0; y < 10; y++)
        {
            for (int x = 0; x < 10; x++)
            {
                original.SetPixel(x, y, new Rgba32((byte)(x * 25), (byte)(y * 25), 128, 255));
            }
        }

        // Save as 24-bit TGA (no alpha)
        using var stream = new MemoryStream();
        original.Save(stream, ImageFormat.Tga);

        stream.Position = 0;
        var loaded = Image.Load(stream, ImageFormat.Tga);

        Assert.AreEqual(original.Width, loaded.Width);
        Assert.AreEqual(original.Height, loaded.Height);

        // Verify pixel data (alpha will be 255 after round-trip)
        var pixel = loaded.GetPixel(5, 5);
        Assert.AreEqual(125, pixel.R);
        Assert.AreEqual(125, pixel.G);
        Assert.AreEqual(128, pixel.B);
        Assert.AreEqual(255, pixel.A);
    }

    #endregion

    #region Header Tests

    [TestMethod]
    public void Tga_ToArray_ProducesValidHeader()
    {
        var image = new Image(100, 50);
        var data = image.ToArray(ImageFormat.Tga);

        // TGA header is 18 bytes
        Assert.IsTrue(data.Length >= 18, "TGA file too short");

        // Verify header values
        Assert.AreEqual(0, data[0], "ID length should be 0");
        Assert.AreEqual(0, data[1], "Color map type should be 0 (none)");
        Assert.AreEqual(2, data[2], "Image type should be 2 (uncompressed true-color)");

        // Width (little-endian at bytes 12-13)
        int width = data[12] | (data[13] << 8);
        Assert.AreEqual(100, width, "Width mismatch");

        // Height (little-endian at bytes 14-15)
        int height = data[14] | (data[15] << 8);
        Assert.AreEqual(50, height, "Height mismatch");
    }

    [TestMethod]
    public void Tga_HasValidFooter()
    {
        var image = new Image(10, 10);
        var data = image.ToArray(ImageFormat.Tga);

        // TGA v2 footer ends with "TRUEVISION-XFILE.\0"
        // Last 26 bytes are footer, signature at bytes 8-25 of footer
        Assert.IsTrue(data.Length >= 26, "TGA file too short for footer");

        int footerStart = data.Length - 26;
        string signature = System.Text.Encoding.ASCII.GetString(data, footerStart + 8, 17);
        Assert.AreEqual("TRUEVISION-XFILE.", signature, "Invalid TGA v2 signature");
        Assert.AreEqual(0, data[data.Length - 1], "Footer should end with null byte");
    }

    #endregion

    #region Encoding Options Tests

    [TestMethod]
    public void Tga_32Bit_EncodesDimensions()
    {
        var image = new Image(256, 128, hasAlpha: true);
        
        using var stream = new MemoryStream();
        Codecs.Tga.TgaCodec.Encode(image, stream, Codecs.Tga.TgaBitsPerPixel.Bit32);
        
        var data = stream.ToArray();
        
        // Check pixel depth byte (16)
        Assert.AreEqual(32, data[16], "Pixel depth should be 32");
    }

    [TestMethod]
    public void Tga_24Bit_EncodesCorrectly()
    {
        var image = new Image(256, 128, hasAlpha: false);
        
        using var stream = new MemoryStream();
        Codecs.Tga.TgaCodec.Encode(image, stream, Codecs.Tga.TgaBitsPerPixel.Bit24);
        
        var data = stream.ToArray();
        
        // Check pixel depth byte (16)
        Assert.AreEqual(24, data[16], "Pixel depth should be 24");
    }

    [TestMethod]
    public void Tga_RLE_ProducesValidOutput()
    {
        // Create image with solid color (should compress well)
        var image = new Image(100, 100);
        for (int y = 0; y < 100; y++)
        {
            for (int x = 0; x < 100; x++)
            {
                image.SetPixel(x, y, new Rgba32(255, 0, 0, 255)); // Solid red
            }
        }

        using var stream = new MemoryStream();
        Codecs.Tga.TgaCodec.Encode(image, stream, Codecs.Tga.TgaBitsPerPixel.Bit32, useRle: true);

        var data = stream.ToArray();

        // Check image type byte (2) - should be 10 for RLE true-color
        Assert.AreEqual(10, data[2], "Image type should be 10 (RLE true-color)");

        // RLE compressed should be smaller than uncompressed for solid color
        // Uncompressed would be header (18) + 100*100*4 pixels + extension (495) + footer (26) = 40539
        // RLE should be much smaller
        Assert.IsTrue(data.Length < 40539, $"RLE compressed size ({data.Length}) should be smaller than uncompressed (40539)");
    }

    [TestMethod]
    public void Tga_RLE_RoundTrip_PreservesData()
    {
        var original = new Image(20, 20);
        // Create a pattern that has both runs and varied pixels
        for (int y = 0; y < 20; y++)
        {
            for (int x = 0; x < 20; x++)
            {
                if (x < 10)
                    original.SetPixel(x, y, new Rgba32(255, 0, 0, 255)); // Red left half
                else
                    original.SetPixel(x, y, new Rgba32(0, 0, 255, 255)); // Blue right half
            }
        }

        using var stream = new MemoryStream();
        Codecs.Tga.TgaCodec.Encode(original, stream, Codecs.Tga.TgaBitsPerPixel.Bit32, useRle: true);

        stream.Position = 0;
        var loaded = Image.Load(stream, ImageFormat.Tga);

        Assert.AreEqual(original.Width, loaded.Width);
        Assert.AreEqual(original.Height, loaded.Height);

        // Verify left half is red
        var leftPixel = loaded.GetPixel(5, 10);
        Assert.AreEqual(255, leftPixel.R);
        Assert.AreEqual(0, leftPixel.G);
        Assert.AreEqual(0, leftPixel.B);

        // Verify right half is blue
        var rightPixel = loaded.GetPixel(15, 10);
        Assert.AreEqual(0, rightPixel.R);
        Assert.AreEqual(0, rightPixel.G);
        Assert.AreEqual(255, rightPixel.B);
    }

    #endregion

    #region Large Image Tests

    [TestMethod]
    public void Tga_LargeImage_HandlesCorrectly()
    {
        var image = new Image(500, 500);
        for (int y = 0; y < 500; y++)
        {
            for (int x = 0; x < 500; x++)
            {
                image.SetPixel(x, y, new Rgba32((byte)(x % 256), (byte)(y % 256), 128, 255));
            }
        }

        using var stream = new MemoryStream();
        image.Save(stream, ImageFormat.Tga);

        stream.Position = 0;
        var loaded = Image.Load(stream, ImageFormat.Tga);

        Assert.AreEqual(500, loaded.Width);
        Assert.AreEqual(500, loaded.Height);

        // Spot check a few pixels
        var pixel1 = loaded.GetPixel(100, 100);
        Assert.AreEqual(100, pixel1.R);
        Assert.AreEqual(100, pixel1.G);
        Assert.AreEqual(128, pixel1.B);

        var pixel2 = loaded.GetPixel(400, 300);
        Assert.AreEqual(144, pixel2.R); // 400 % 256 = 144
        Assert.AreEqual(44, pixel2.G);  // 300 % 256 = 44
        Assert.AreEqual(128, pixel2.B);
    }

    [TestMethod]
    public void Tga_NonSquare_HandlesCorrectly()
    {
        var image = new Image(100, 50);
        for (int y = 0; y < 50; y++)
        {
            for (int x = 0; x < 100; x++)
            {
                image.SetPixel(x, y, new Rgba32((byte)x, (byte)y, 0, 255));
            }
        }

        using var stream = new MemoryStream();
        image.Save(stream, ImageFormat.Tga);

        stream.Position = 0;
        var loaded = Image.Load(stream, ImageFormat.Tga);

        Assert.AreEqual(100, loaded.Width);
        Assert.AreEqual(50, loaded.Height);
    }

    #endregion

    #region Edge Cases

    [TestMethod]
    public void Tga_SinglePixel_Works()
    {
        var image = new Image(1, 1);
        image.SetPixel(0, 0, new Rgba32(42, 128, 200, 255));

        using var stream = new MemoryStream();
        image.Save(stream, ImageFormat.Tga);

        stream.Position = 0;
        var loaded = Image.Load(stream, ImageFormat.Tga);

        Assert.AreEqual(1, loaded.Width);
        Assert.AreEqual(1, loaded.Height);

        var pixel = loaded.GetPixel(0, 0);
        Assert.AreEqual(42, pixel.R);
        Assert.AreEqual(128, pixel.G);
        Assert.AreEqual(200, pixel.B);
    }

    [TestMethod]
    public void Tga_AllBlack_Works()
    {
        var image = new Image(10, 10);
        // Default pixels are black (0,0,0,0), set alpha to 255
        for (int y = 0; y < 10; y++)
        {
            for (int x = 0; x < 10; x++)
            {
                image.SetPixel(x, y, new Rgba32(0, 0, 0, 255));
            }
        }

        using var stream = new MemoryStream();
        image.Save(stream, ImageFormat.Tga);

        stream.Position = 0;
        var loaded = Image.Load(stream, ImageFormat.Tga);

        var pixel = loaded.GetPixel(5, 5);
        Assert.AreEqual(0, pixel.R);
        Assert.AreEqual(0, pixel.G);
        Assert.AreEqual(0, pixel.B);
        Assert.AreEqual(255, pixel.A);
    }

    [TestMethod]
    public void Tga_AllWhite_Works()
    {
        var image = new Image(10, 10);
        for (int y = 0; y < 10; y++)
        {
            for (int x = 0; x < 10; x++)
            {
                image.SetPixel(x, y, Rgba32.White);
            }
        }

        using var stream = new MemoryStream();
        image.Save(stream, ImageFormat.Tga);

        stream.Position = 0;
        var loaded = Image.Load(stream, ImageFormat.Tga);

        var pixel = loaded.GetPixel(5, 5);
        Assert.AreEqual(255, pixel.R);
        Assert.AreEqual(255, pixel.G);
        Assert.AreEqual(255, pixel.B);
    }

    [TestMethod]
    public void Tga_GradientImage_PreservesValues()
    {
        // Create a gradient to test all color values
        var image = new Image(256, 1);
        for (int x = 0; x < 256; x++)
        {
            image.SetPixel(x, 0, new Rgba32((byte)x, (byte)x, (byte)x, 255));
        }

        using var stream = new MemoryStream();
        image.Save(stream, ImageFormat.Tga);

        stream.Position = 0;
        var loaded = Image.Load(stream, ImageFormat.Tga);

        // Check some gradient values
        Assert.AreEqual(0, loaded.GetPixel(0, 0).R);
        Assert.AreEqual(127, loaded.GetPixel(127, 0).R);
        Assert.AreEqual(255, loaded.GetPixel(255, 0).R);
    }

    #endregion

    #region Format Detection Tests

    [TestMethod]
    public void Tga_FormatDetection_ByExtension()
    {
        var image = new Image(5, 5);
        var data = image.ToArray(ImageFormat.Tga);

        // TGA doesn't have a magic number, but we test that our extension-based
        // detection works by saving and loading
        var tempPath = Path.GetTempFileName();
        var tgaPath = Path.ChangeExtension(tempPath, ".tga");

        try
        {
            File.WriteAllBytes(tgaPath, data);
            var loaded = Image.Load(tgaPath);
            Assert.AreEqual(5, loaded.Width);
            Assert.AreEqual(5, loaded.Height);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            if (File.Exists(tgaPath)) File.Delete(tgaPath);
        }
    }

    #endregion

    #region Null/Invalid Input Tests

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Tga_Encode_NullImage_Throws()
    {
        using var stream = new MemoryStream();
        Codecs.Tga.TgaCodec.Encode(null!, stream);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Tga_Encode_NullStream_Throws()
    {
        var image = new Image(5, 5);
        Codecs.Tga.TgaCodec.Encode(image, null!);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Tga_Decode_NullStream_Throws()
    {
        Codecs.Tga.TgaCodec.Decode(null!);
    }

    #endregion
}
