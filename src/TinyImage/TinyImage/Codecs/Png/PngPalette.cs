namespace TinyImage.Codecs.Png;

/// <summary>
/// PNG color palette.
/// </summary>
internal sealed class PngPalette
{
    public bool HasAlphaValues { get; private set; }
    public byte[] Data { get; }

    public PngPalette(byte[] data)
    {
        Data = new byte[data.Length * 4 / 3];
        var dataIndex = 0;
        for (var i = 0; i < data.Length; i += 3)
        {
            Data[dataIndex++] = data[i];
            Data[dataIndex++] = data[i + 1];
            Data[dataIndex++] = data[i + 2];
            Data[dataIndex++] = 255;
        }
    }

    public void SetAlphaValues(byte[] bytes)
    {
        HasAlphaValues = true;
        for (var i = 0; i < bytes.Length; i++)
        {
            Data[i * 4 + 3] = bytes[i];
        }
    }

    public Rgba32 GetPixel(int index)
    {
        var start = index * 4;
        return new Rgba32(Data[start], Data[start + 1], Data[start + 2], Data[start + 3]);
    }
}
