using System.Collections.Generic;
using PiaSharp.Core.Objects;
using Emgu.CV;
using Emgu.CV.Structure;

namespace PiaSharp.Core
{
    public class Context
    {
        public Image<Lab, double> Input { get; set; }
        public Dictionary<int, PaletteColor> Palette { get; set; }
        public Dictionary<int, PaletteCluster> PaletteClusters { get; set; }
        public double Temperature { get; set; }
        public double TemperatureFinal { get; set; }
        public Dimension OutputSize { get; set; }
        public Superpixel[][] Superpixels { get; set; }
        public int K { get; set; }
        public int KMax { get; set; }
        public double EpsilonP { get; set; }
        public double EpsilonC { get; set; }
        public static bool HasConverged { get; set; }
        public static int Iteration = 0;
        public static int PreviousChangeIteration = 0;
    }
}
