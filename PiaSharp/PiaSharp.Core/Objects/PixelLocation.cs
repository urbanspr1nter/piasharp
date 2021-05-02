namespace PiaSharp.Core.Objects
{
    public class PixelLocation
    {
        public int Row { get; set; }
        public int Column { get; set; }

        public PixelLocation(int row, int column)
        {
            Row = row;
            Column = column;
        }
    }
}
