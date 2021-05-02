namespace PiaSharp.Core.Objects
{
    public class LabColor
    {
        public double L { get; set; }
        public double A { get; set; }
        public double B { get; set; }

        public LabColor(double l, double a, double b)
        {
            L = l;
            A = a;
            B = b;
        }

        public LabColor Copy()
        {
            return new LabColor(L, A, B);
        }
    }
}
