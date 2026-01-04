namespace TinyImage.Demo;

class Program
{
    static void Main(string[] args)
    {
        //Process("dice.png");
        //Process("stone.jpg");
        //Process("earth.gif");
        //Process("globe.jp2");
        //Process("three.bmp");
        //Process("lena.bmp");
        //Process("landscape.pbm");
        //Process("cat.webp");
        //Process("matrix.webp");
        Process("autumn.tif");
        
        void Process(string name)
        {
            Image image = Image.Load(Path.Join("imgs", name));
            Image copy = image.Resize(128, 128);
            copy.Save($"{Path.GetFileNameWithoutExtension(name)}_resized{Path.GetExtension(name)}");
            image.Save($"{Path.GetFileNameWithoutExtension(name)}_original{Path.GetExtension(name)}");
        }
        
        int z = 0;
    }
}