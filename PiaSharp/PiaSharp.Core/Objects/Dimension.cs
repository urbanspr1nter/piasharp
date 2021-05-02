namespace PiaSharp.Core.Objects
{
    public class Dimension
    {
        public int Width { get; set; }
        public int Height { get; set; }

        public Dimension(int height, int width)
        {
            Height = height;
            Width = width;
        }
    }
}
