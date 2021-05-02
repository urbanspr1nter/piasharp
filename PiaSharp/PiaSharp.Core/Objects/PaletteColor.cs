using System;

namespace PiaSharp.Core.Objects
{
    public class PaletteColor
    {
        public LabColor Color { get; set; }

        public double Probability { get; set; }

        public PaletteColor(LabColor color, double probability)
        {
            Color = color;
            Probability = probability;
        }

        public override string ToString()
        {
            return $"PaletteColor::{{Color:[{Color.L},{Color.A},{Color.B}], Probability:{Probability}}}";
        }
    }
}
