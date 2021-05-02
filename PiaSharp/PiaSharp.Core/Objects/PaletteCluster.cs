namespace PiaSharp.Core.Objects
{
    public class PaletteCluster
    {
        public int First { get; }
        public int Second { get; }
        public int Length { get; }

        public PaletteCluster(int first)
        {
            First = first;
            Second = int.MaxValue;
            Length = 1;
        }
        public PaletteCluster(int first, int second)
        {
            First = first;
            Second = second;
            Length = 2;
        }
    }
}
