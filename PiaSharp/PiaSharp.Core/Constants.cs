using System.Collections.Generic;
using PiaSharp.Core.Objects;

namespace PiaSharp.Core
{
    public static class Constants
    {
        // The amount of iterations on the current temperature. This is to ensure that the 
        // program will eventually terminate.
        public static int ChangeIterationsThreshold = 1000;

        public static int m = 45;

        public static List<Dimension> NeighborhoodDeltasForSuperpixelRefine
            = new List<Dimension>() {
                new Dimension(-1, -1),
                new Dimension(-1, 0),
                new Dimension(-1, 1),
                new Dimension(0, -1),
                new Dimension(0, 0),
                new Dimension(0, 1),
                new Dimension(1, -1),
                new Dimension(1, 0),
                new Dimension(1, 1)
            };

        public static List<Dimension> NeighborhoodDeltasForLaplacianSmooth 
            = new List<Dimension>() {
                new Dimension(0, 1),
                new Dimension(0, -1),
                new Dimension(-1, 0),
                new Dimension(1, 0)
            };

        public static List<Dimension> NeighborhoodDeltasForMeanSuperpixelColor
            = new List<Dimension>() {
                new Dimension(-1, -1),
                new Dimension(-1, 0),
                new Dimension(-1, 1),
                new Dimension(0, -1),
                new Dimension(0, 0),
                new Dimension(0, 1),
                new Dimension(1, -1),
                new Dimension(1, 0),
                new Dimension(1, 1)
            };
    }
}
