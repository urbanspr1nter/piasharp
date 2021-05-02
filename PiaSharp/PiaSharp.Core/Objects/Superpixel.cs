using System.Collections.Generic;

namespace PiaSharp.Core.Objects
{
    /**
     * A superpixel is the individual pixel of the output image. 
     * It is represented by a SINGLE position and SINGLE color at its core.
     */
    public class Superpixel
    {
        // Location of the superpixel
        public PixelLocation Location { get; set; }

        // The key that can be used to reference back to the global palette
        // to obtain the superpixel's palette color
        public int PaletteColorKey { get; set; }

        // The computed average color of the group of input pixels
        // which are associated with this superpixel.
        public LabColor Color { get; set; }

        // Internal mappings of the input image pixels
        public List<PixelLocation> Pixels { get; set; }

        // The uniform probability of this superpixel. Default 1 / N
        public double Probability { get; set; }

        // Every single superpixel has an associated probability to some color
        // within the global color palette. The key is the palette color key, and
        // the value represents the probability to that palette color
        public Dictionary<int, double> PaletteProbabilityMap;

        private Superpixel()
        {
            Pixels = new List<PixelLocation>();
            Color = new LabColor(0, 0, 0);
            PaletteColorKey = 0;
            Location = new PixelLocation(0, 0);
            PaletteProbabilityMap = new Dictionary<int , double>();
            Probability = 1;
        }

        public Superpixel(int paletteColorKey, PixelLocation location)
        {
            Color = new LabColor(0, 0, 0);
            Location = location;
            PaletteColorKey = paletteColorKey;
            PaletteProbabilityMap = new Dictionary<int, double>();
            Pixels = new List<PixelLocation>();
            Probability = 1;
        }
    }
}
