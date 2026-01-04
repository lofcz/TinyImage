namespace TinyImage.Demo;

class Program
{
    static void Main(string[] args)
    {
        Process("dice.png");
        Process("stone.jpg");
        Process("earth.gif");
        
        void Process(string name)
        {
            Image image = Image.Load(Path.Join("imgs", name));
            Image copy = image.Resize(128, 128);
            copy.Save($"{Path.GetFileNameWithoutExtension(name)}_resized{Path.GetExtension(name)}");       
        }
        
        int z = 0;
    }
}