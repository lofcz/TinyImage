namespace TinyImage.Tests;

[TestClass]
public sealed class ResizeTests
{
    [TestMethod]
    public void Resize_ScaleDown_ReducesDimensions()
    {
        var image = new Image(100, 100);
        
        var resized = image.Resize(50, 50);
        
        Assert.AreEqual(50, resized.Width);
        Assert.AreEqual(50, resized.Height);
    }

    [TestMethod]
    public void Resize_ScaleUp_IncreasesDimensions()
    {
        var image = new Image(50, 50);
        
        var resized = image.Resize(100, 100);
        
        Assert.AreEqual(100, resized.Width);
        Assert.AreEqual(100, resized.Height);
    }

    [TestMethod]
    public void Resize_NearestNeighbor_PreservesPixelValues()
    {
        var image = new Image(2, 2);
        image.SetPixel(0, 0, new Rgba32(255, 0, 0, 255));
        image.SetPixel(1, 0, new Rgba32(0, 255, 0, 255));
        image.SetPixel(0, 1, new Rgba32(0, 0, 255, 255));
        image.SetPixel(1, 1, new Rgba32(255, 255, 255, 255));
        
        var resized = image.Resize(4, 4, ResizeMode.NearestNeighbor);
        
        // Top-left quadrant should be red
        Assert.AreEqual(new Rgba32(255, 0, 0, 255), resized.GetPixel(0, 0));
        Assert.AreEqual(new Rgba32(255, 0, 0, 255), resized.GetPixel(1, 0));
        Assert.AreEqual(new Rgba32(255, 0, 0, 255), resized.GetPixel(0, 1));
        Assert.AreEqual(new Rgba32(255, 0, 0, 255), resized.GetPixel(1, 1));
    }

    [TestMethod]
    public void Resize_Bilinear_InterpolatesColors()
    {
        var image = new Image(2, 2);
        image.SetPixel(0, 0, new Rgba32(0, 0, 0, 255));
        image.SetPixel(1, 0, new Rgba32(255, 255, 255, 255));
        image.SetPixel(0, 1, new Rgba32(0, 0, 0, 255));
        image.SetPixel(1, 1, new Rgba32(255, 255, 255, 255));
        
        var resized = image.Resize(3, 3, ResizeMode.Bilinear);
        
        // Center pixel should be interpolated (around 128)
        var center = resized.GetPixel(1, 1);
        Assert.IsTrue(center.R > 100 && center.R < 156, $"Expected R around 128, got {center.R}");
        Assert.IsTrue(center.G > 100 && center.G < 156, $"Expected G around 128, got {center.G}");
        Assert.IsTrue(center.B > 100 && center.B < 156, $"Expected B around 128, got {center.B}");
    }

    [TestMethod]
    public void Resize_Bicubic_ProducesResult()
    {
        var image = new Image(10, 10);
        for (int y = 0; y < 10; y++)
            for (int x = 0; x < 10; x++)
                image.SetPixel(x, y, new Rgba32((byte)(x * 25), (byte)(y * 25), 128, 255));
        
        var resized = image.Resize(20, 20, ResizeMode.Bicubic);
        
        Assert.AreEqual(20, resized.Width);
        Assert.AreEqual(20, resized.Height);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentOutOfRangeException))]
    public void Resize_ZeroWidth_ThrowsException()
    {
        var image = new Image(100, 100);
        _ = image.Resize(0, 50);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentOutOfRangeException))]
    public void Resize_ZeroHeight_ThrowsException()
    {
        var image = new Image(100, 100);
        _ = image.Resize(50, 0);
    }

    [TestMethod]
    public void Resize_DefaultMode_UsesBilinear()
    {
        var image = new Image(10, 10);
        
        // Should not throw - default mode is bilinear
        var resized = image.Resize(20, 20);
        
        Assert.AreEqual(20, resized.Width);
    }

    [TestMethod]
    public void Resize_PreservesHasAlpha()
    {
        var imageWithAlpha = new Image(10, 10, hasAlpha: true);
        var imageWithoutAlpha = new Image(10, 10, hasAlpha: false);
        
        var resizedWithAlpha = imageWithAlpha.Resize(5, 5);
        var resizedWithoutAlpha = imageWithoutAlpha.Resize(5, 5);
        
        Assert.IsTrue(resizedWithAlpha.HasAlpha);
        Assert.IsFalse(resizedWithoutAlpha.HasAlpha);
    }

    [TestMethod]
    public void Resize_MultiFrame_ResizesAllFrames()
    {
        // Create animated image with multiple frames
        var image = new Image(20, 20);
        image.LoopCount = 2;
        image.Frames.RootFrame.Duration = TimeSpan.FromMilliseconds(100);
        image.Frames.RootFrame.SetPixel(0, 0, new Rgba32(255, 0, 0, 255));

        var frame2 = image.Frames.AddFrame(20, 20);
        frame2.Duration = TimeSpan.FromMilliseconds(200);
        frame2.SetPixel(0, 0, new Rgba32(0, 255, 0, 255));

        var frame3 = image.Frames.AddFrame(20, 20);
        frame3.Duration = TimeSpan.FromMilliseconds(300);
        frame3.SetPixel(0, 0, new Rgba32(0, 0, 255, 255));

        // Resize
        var resized = image.Resize(10, 10);

        // Verify all frames were resized
        Assert.AreEqual(3, resized.Frames.Count);
        Assert.AreEqual(10, resized.Frames[0].Width);
        Assert.AreEqual(10, resized.Frames[1].Width);
        Assert.AreEqual(10, resized.Frames[2].Width);

        // Verify frame durations are preserved
        Assert.AreEqual(TimeSpan.FromMilliseconds(100), resized.Frames[0].Duration);
        Assert.AreEqual(TimeSpan.FromMilliseconds(200), resized.Frames[1].Duration);
        Assert.AreEqual(TimeSpan.FromMilliseconds(300), resized.Frames[2].Duration);

        // Verify loop count is preserved
        Assert.AreEqual(2, resized.LoopCount);
    }
}
