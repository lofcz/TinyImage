namespace TinyImage.Tests;

[TestClass]
public sealed class ImageTests
{
    [TestMethod]
    public void CreateImage_WithValidDimensions_CreatesImage()
    {
        var image = new Image(100, 50);
        
        Assert.AreEqual(100, image.Width);
        Assert.AreEqual(50, image.Height);
        Assert.IsTrue(image.HasAlpha);
    }

    [TestMethod]
    public void CreateImage_WithoutAlpha_SetsHasAlphaFalse()
    {
        var image = new Image(100, 50, hasAlpha: false);
        
        Assert.IsFalse(image.HasAlpha);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentOutOfRangeException))]
    public void CreateImage_WithZeroWidth_ThrowsException()
    {
        _ = new Image(0, 50);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentOutOfRangeException))]
    public void CreateImage_WithZeroHeight_ThrowsException()
    {
        _ = new Image(50, 0);
    }

    [TestMethod]
    public void GetSetPixel_ValidCoordinates_WorksCorrectly()
    {
        var image = new Image(10, 10);
        var color = new Rgba32(255, 128, 64, 255);
        
        image.SetPixel(5, 5, color);
        var result = image.GetPixel(5, 5);
        
        Assert.AreEqual(color, result);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentOutOfRangeException))]
    public void GetPixel_OutOfBounds_ThrowsException()
    {
        var image = new Image(10, 10);
        _ = image.GetPixel(10, 5);
    }

    [TestMethod]
    public void Clone_CreatesDeepCopy()
    {
        var original = new Image(10, 10);
        var color = new Rgba32(255, 0, 0, 255);
        original.SetPixel(0, 0, color);
        
        var clone = original.Clone();
        clone.SetPixel(0, 0, Rgba32.Black);
        
        Assert.AreEqual(color, original.GetPixel(0, 0));
        Assert.AreEqual(Rgba32.Black, clone.GetPixel(0, 0));
    }
}
