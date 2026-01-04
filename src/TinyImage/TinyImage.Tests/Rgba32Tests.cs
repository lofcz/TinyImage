namespace TinyImage.Tests;

[TestClass]
public sealed class Rgba32Tests
{
    [TestMethod]
    public void Constructor_WithComponents_SetsCorrectValues()
    {
        var color = new Rgba32(10, 20, 30, 40);
        
        Assert.AreEqual(10, color.R);
        Assert.AreEqual(20, color.G);
        Assert.AreEqual(30, color.B);
        Assert.AreEqual(40, color.A);
    }

    [TestMethod]
    public void Constructor_WithDefaultAlpha_SetsAlphaTo255()
    {
        var color = new Rgba32(10, 20, 30);
        
        Assert.AreEqual(255, color.A);
    }

    [TestMethod]
    public void Constructor_FromPackedValue_ExtractsCorrectComponents()
    {
        uint packed = 0xAABBCCDD; // R=AA, G=BB, B=CC, A=DD
        var color = new Rgba32(packed);
        
        Assert.AreEqual(0xAA, color.R);
        Assert.AreEqual(0xBB, color.G);
        Assert.AreEqual(0xCC, color.B);
        Assert.AreEqual(0xDD, color.A);
    }

    [TestMethod]
    public void PackedValue_ReturnsCorrectValue()
    {
        var color = new Rgba32(0xAA, 0xBB, 0xCC, 0xDD);
        
        Assert.AreEqual(0xAABBCCDDu, color.PackedValue);
    }

    [TestMethod]
    public void Equals_SameValues_ReturnsTrue()
    {
        var color1 = new Rgba32(10, 20, 30, 40);
        var color2 = new Rgba32(10, 20, 30, 40);
        
        Assert.IsTrue(color1.Equals(color2));
        Assert.IsTrue(color1 == color2);
    }

    [TestMethod]
    public void Equals_DifferentValues_ReturnsFalse()
    {
        var color1 = new Rgba32(10, 20, 30, 40);
        var color2 = new Rgba32(10, 20, 30, 50);
        
        Assert.IsFalse(color1.Equals(color2));
        Assert.IsTrue(color1 != color2);
    }

    [TestMethod]
    public void Transparent_HasZeroAlpha()
    {
        Assert.AreEqual(0, Rgba32.Transparent.A);
    }

    [TestMethod]
    public void Black_IsFullyOpaqueBlack()
    {
        Assert.AreEqual(0, Rgba32.Black.R);
        Assert.AreEqual(0, Rgba32.Black.G);
        Assert.AreEqual(0, Rgba32.Black.B);
        Assert.AreEqual(255, Rgba32.Black.A);
    }

    [TestMethod]
    public void White_IsFullyOpaqueWhite()
    {
        Assert.AreEqual(255, Rgba32.White.R);
        Assert.AreEqual(255, Rgba32.White.G);
        Assert.AreEqual(255, Rgba32.White.B);
        Assert.AreEqual(255, Rgba32.White.A);
    }
}
