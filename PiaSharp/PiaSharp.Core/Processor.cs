using System;
using System.Linq;
using System.Collections.Generic;
using PiaSharp.Core.Objects;
using PiaSharp.Core.DebugUtils;
using Emgu.CV;
using Emgu.CV.Structure;
using Accord.Statistics.Analysis;

namespace PiaSharp.Core
{
    public class Processor
    {
        private int LatestPaletteKey;
        private PaletteColor InitialMeanColor;
        private readonly Context Context;
        private double[] PcaDelta;
        private ILogger Logger;

        public double EpsilonP
        {
            get
            {
                return Context.EpsilonP;
            }
        }

        public double Temperature 
        { 
            get 
            { 
                return Context.Temperature;  
            } 
            set { 
                Context.Temperature = value;  
            } 
        }

        public double TemperatureFinal
        {
            get
            {
                return Context.TemperatureFinal;
            }
        }

        public Image<Lab, double> Input
        {
            get
            {
                return Context.Input;
            }
        }

        public Dimension OutputSize
        {
            get
            {
                return Context.OutputSize;
            }
        }

        public Superpixel[][] Superpixels
        {
            get
            {
                return Context.Superpixels;
            }
        }

        public Dictionary<int, PaletteColor> Palette
        {
            get
            {
                return Context.Palette;
            }
        }

        public int CurrentPaletteSize
        {
            get
            {
                return Context.Palette.Keys.Count;
            }
        }

        public int K {
            get { 
                return Context.K;
            }
        }

        public int KMax
        {
            get
            {
                return Context.KMax;
            }
        }

        public Processor(ILogger logger, string inputFile, Dimension outputSize, int totalColors)
        {
            LatestPaletteKey = 0;
            Logger = logger;
            Context = new Context();

            // Set the input and output params
            Image<Lab, double> input = CreateImageFromFilename(inputFile);
            Context.Input = input;
            Context.OutputSize = outputSize;

            // Initialize the palette to contain a "single" color -- the average of the image
            PaletteColor meanColor = GetInitialMeanColor(Context.Input);
            PaletteColor initPaletteColor1 = new PaletteColor(meanColor.Color.Copy(), 0.5);
            PaletteColor initPaletteColor2 = new PaletteColor(meanColor.Color.Copy(), 0.5);
            
            Context.Palette = new Dictionary<int, PaletteColor>();
            var firstKey = AddToPalette(initPaletteColor1);
            var secondKey = AddToPalette(initPaletteColor2);

            InitialMeanColor = meanColor;

            // Initialize the palette cluster
            Context.PaletteClusters = new Dictionary<int, PaletteCluster>();
            var initialCluster = new PaletteCluster(firstKey, secondKey);
            Context.PaletteClusters.Add(firstKey, initialCluster);

            // Initialize the superpixels to a regular grid
            InitializeSuperpixels();

            // We initialize the temperature to be a very high value so that each superpixel is likely
            // to be able to be associated with any of the current palette colors. We can think of each
            // color in the palette as an item within a cluster.
            Context.Temperature = FindCriticalTemperature();

            Context.TemperatureFinal = 1.0;
            Context.EpsilonP = 1.0;
            Context.EpsilonC = 0.125;
            Context.K = 1;

            Context.KMax = totalColors;

            // Perturb the secondary color in the initial palette so subsequent splits can happen
            // correctly.
            PcaDelta = FindPcaDeltas();
            Context.Palette[1] = PerturbColor(Context.Palette[1]);
        }

        /**
         * Initializes a regular grid as the initial grid of superpixels. By regular, it means that 
         * each superpixel is evenly spaced at some QxR dimension. 
         */
        private void InitializeSuperpixels()
        {
            // The initial probability for which a superpixel is chosen is uniform. P(ps) = 1 / N
            double initialUniformProbability = 1.0 / (Context.OutputSize.Width * Context.OutputSize.Height);

            Superpixel[][] superpixels = new Superpixel[Context.OutputSize.Height][];
            for (int i = 0; i < Context.OutputSize.Height; i++)
            {
                superpixels[i] = new Superpixel[Context.OutputSize.Width];
            }

            // Initialize the grid as a regular grid
            int[] gridX = new int[Context.OutputSize.Width];
            int[] gridY = new int[Context.OutputSize.Height];

            for(int i = 0; i < gridX.Length; i++)
            {
                gridX[i] = (int)(Math.Floor((double)(i * Context.Input.Width) / Context.OutputSize.Width));
            }
            for (int i = 0; i < gridY.Length; i++)
            {
                gridY[i] = (int)(Math.Floor((double)(i * Context.Input.Height) / Context.OutputSize.Height));
            }

            // Initialize every superpixel in this grid
            for (int row = 0; row < gridY.Length; row++)
            {
                for(int col = 0; col < gridX.Length; col++)
                {
                    superpixels[row][col] = new Superpixel(0, new PixelLocation(row, col));
                    superpixels[row][col].Probability = initialUniformProbability;

                    foreach (var k in Context.Palette.Keys)
                    {
                        superpixels[row][col].PaletteProbabilityMap.Add(k, 1.0 / Context.Palette.Keys.Count);
                    }
                }
            }

            // Assign pixels to regular grid
            int ratio = (int)Math.Floor((double)(Context.Input.Width / Context.OutputSize.Width));

            for (int row = 0; row < ratio * Context.OutputSize.Height; row++)
            {
                for (int col = 0; col < ratio * Context.OutputSize.Width; col++)
                {
                    int i = (int)Math.Floor((double)row / ratio);
                    int j = (int)Math.Floor((double)col / ratio);

                    superpixels[i][j].Pixels.Add(new PixelLocation(row, col));
                }
            }

            // Obtain the mean color from all pixels currently assigned to the superpixel
            for (int row = 0; row < Context.OutputSize.Height; row++)
            {
                for (int col = 0; col < Context.OutputSize.Width; col++)
                {
                    superpixels[row][col].Color = GetSuperpixelMeanColorFromAssignedPixels(superpixels[row][col]);
                }
            }

            Context.Superpixels = superpixels;
        }


        /**
         * To refine a superpixel means to reassociate input pixels to superpixels after the palette has been
         * refined from the previous iteration. The cost relationship between the superpixel, and pixel is recomputed
         * and thus using new information, reassigns the pixel to the superpixel in which cost is minimized.
         */
        public void RefineSuperpixels()
        {
            // Clear the old list of input image pixels associated with this superpixel. The list will
            // be updated with new pixel locations in this routine.
            for(int r = 0; r < Context.OutputSize.Height; r++)
            {
                for(int c = 0; c < Context.OutputSize.Width; c++)
                {
                    Context.Superpixels[r][c].Pixels.Clear();
                }
            }

            // Calculate the cost of a pixel to superpixel within a 3x3 neighborhood.
            // Each single iteration of refining superpixels is only concerned with displacing a current 
            // input pixel at a magnitude of a single pixel.
            var neighborhoodDeltas = Constants.NeighborhoodDeltasForSuperpixelRefine;

            // Iterate through every pixel in the INPUT IMAGE, and assign it to a super pixel with the best cost.
            for (int inR = 0; inR < Context.Input.Height; inR++)
            {
                for (int inC = 0; inC < Context.Input.Width; inC++)
                {
                    // Using the current input image coordinates, compute the superpixel location that this input image 
                    // coordinate is likely to fall into
                    int spRow = (inR * Context.OutputSize.Height) / Context.Input.Height;
                    int spCol = (inC * Context.OutputSize.Width) / Context.Input.Width;

                    // Recompute the cost, and get the minimum cost for which a pixel is associated with a superpixel. For this, 
                    // an initial value of minCost must be assigned. 
                    PixelLocation currBestSpLocation = new PixelLocation(spRow, spCol);
                    Superpixel currBestSp = Context.Superpixels[currBestSpLocation.Row][currBestSpLocation.Column];
                    PixelLocation imageLocation = new PixelLocation(inR, inC);

                    // Initialize the minimum cost to be the current location in input image
                    double minCost = GetAssociatedSuperpixelCost(currBestSp, Context.OutputSize, Context.Input, imageLocation, currBestSpLocation);

                    foreach (var n in neighborhoodDeltas)
                    {
                        PixelLocation candidateSpLocation = new PixelLocation(currBestSpLocation.Row + n.Height, currBestSpLocation.Column + n.Width);
                        if (!IsLocationInGrid(candidateSpLocation.Row, candidateSpLocation.Column))
                        {
                            continue;
                        }

                        var currSp = Context.Superpixels[candidateSpLocation.Row][candidateSpLocation.Column];
                        var currCost = GetAssociatedSuperpixelCost(currSp, Context.OutputSize, Context.Input, imageLocation, candidateSpLocation);

                        // Minimize the cost
                        if (currCost < minCost)
                        {
                            minCost = currCost;
                            currBestSpLocation = candidateSpLocation;
                        }
                    }

                    // Based on the superpixel with the min cost associated for this pixel, add the pixel 
                    // to the list of pixels for that superpixel
                    Context.Superpixels[currBestSpLocation.Row][currBestSpLocation.Column].Pixels.Add(imageLocation);
                }
            }

            // Update the output location and color of the superpixel now that the list of pixels have changed.
            for (int r = 0; r < Context.Superpixels.Length; r++)
            {
                for (int c = 0; c < Context.Superpixels[r].Length; c++)
                {
                    var ps = Context.Superpixels[r][c];

                    // Update the position
                    if(ps.Pixels.Count > 0)
                    {
                        int x = 0;
                        int y = 0;

                        foreach (PixelLocation loc in ps.Pixels)
                        {
                            x += loc.Column;
                            y += loc.Row;
                        }

                        x /= ps.Pixels.Count;
                        y /= ps.Pixels.Count;

                        ps.Location = new PixelLocation(y, x);
                    }

                    // Update its color
                    if(ps.Pixels.Count > 0)
                    {
                        ps.Color = GetSuperpixelMeanColorFromAssignedPixels(ps);
                    }
                }
            }


            // Performs Laplacian Smoothing to maintain the regular grid-like structure for output.
            // Due to processing in a 3x3 neighborhood, the superpixels will start to form as a hexagonal shape.
            // This may cause distortions, so we want to remap and shift some pixels so that 4-connected neighborhoods
            // can be maintained
            PixelLocation[][] newLocations = new PixelLocation[Context.OutputSize.Height][];
            for (int i = 0; i < Context.OutputSize.Height; i++)
            {
                newLocations[i] = new PixelLocation[Context.OutputSize.Width];
            }

            var lapNeighborhoodDeltas = Constants.NeighborhoodDeltasForLaplacianSmooth;

            for(int r = 0; r < Context.OutputSize.Height; r++)
            {
                for(int c = 0; c < Context.OutputSize.Width; c++)
                {
                    var ps = Context.Superpixels[r][c];
                    var newOutputLocation = new PixelLocation(ps.Location.Row, ps.Location.Column);

                    double sumX = 0;
                    double sumY = 0;
                    int n = 0;

                    bool hasSkippedSmooth = false;

                    foreach(var d in lapNeighborhoodDeltas)
                    {
                        var candidateSpLocation = new PixelLocation(ps.Location.Row + d.Height, ps.Location.Column + d.Width);
                        if (!IsLocationInGrid(candidateSpLocation.Row, candidateSpLocation.Column))
                        {
                            // Should not smooth at all if there is a boundary issue.
                            hasSkippedSmooth = true;
                            break;
                        }

                        n++;
                        sumX += candidateSpLocation.Column;
                        sumY += candidateSpLocation.Row;
                    }

                    if (!hasSkippedSmooth)
                    {
                        double avgX = sumX / n;
                        double avgY = sumY / n;

                        // Each superpixel's (x, y) position is moved 40% from its current position to the 60% of the average
                        // position.
                        newOutputLocation.Row = (int)(0.4 * avgY + 0.6 * ps.Location.Row);
                        newOutputLocation.Column = (int)(0.4 * avgX + 0.6 * ps.Location.Column);
                    }

                    newLocations[r][c] = newOutputLocation;
                }
            }

            // Bilateral Color Smoothing: this results in m_s', which is the color 
            // corresponding to the superpixel, but not yet the palette color of the superpixel.
            // the latter happens in the Associate step.
            LabColor[][] newSuperpixelColors = new LabColor[Context.OutputSize.Height][];
            for (int i = 0; i < Context.OutputSize.Height; i++)
            {
                newSuperpixelColors[i] = new LabColor[Context.OutputSize.Width];
            }

            for (int r = 0; r < Context.OutputSize.Height; r++)
            {
                for(int c = 0; c < Context.OutputSize.Width; c++)
                {
                    var ps = Context.Superpixels[r][c];

                    double sigma = 0.87;

                    var neighborhood = Constants.NeighborhoodDeltasForMeanSuperpixelColor;

                    double weight = 0;
                    LabColor sumColor = new LabColor(0, 0, 0);

                    foreach(var n in neighborhood)
                    {
                        if (!IsLocationInGrid(r + n.Height, c + n.Width))
                        {
                            continue;
                        }

                        var candidatePs = Context.Superpixels[r + n.Height][c + n.Width];

                        double cdist = ColorDistance(ps.Color, candidatePs.Color);
                        double weightColor = GaussianFunc(cdist, sigma, 0.0);
                        double posDist = Math.Sqrt(Math.Pow(r - (r + n.Height), 2) + Math.Pow(c - (c + n.Width), 2));
                        double weightPos = GaussianFunc(posDist, sigma, 0.0);
                        double weightTotal = weightColor * weightPos;
                        weight += weightTotal;
                        sumColor.L += candidatePs.Color.L * weightTotal;
                        sumColor.A += candidatePs.Color.A * weightTotal;
                        sumColor.B += candidatePs.Color.B * weightTotal;
                    }

                    sumColor.L /= weight;
                    sumColor.A /= weight;
                    sumColor.B /= weight;

                    newSuperpixelColors[r][c] = sumColor;
                }
            }

            // Copy the newLocations and newSuperpixelColors of the superpixel to the newSp
            // structure.
            for (int r = 0; r < Context.OutputSize.Height; r++)
            {
                for (int c = 0; c < Context.OutputSize.Width; c++)
                {
                    Context.Superpixels[r][c].Location = newLocations[r][c];
                    Context.Superpixels[r][c].Color = newSuperpixelColors[r][c];
                }
            }
        }


        /**
         * 1. Each superpixel in the output is reassociated with new conditional probabilities to palette colors. This is to make 
         *    sure that the palette colors referenced by the superpixels stays up to date after the palette has been refined in the 
         *    previous iteration.
         * 
         * 2. The individual probability P(ck) for a single palette color can be verbally expressed as
         *      - For every superpixel in the output, calculate the sum for which the current superpixel 
         *        is likely to choose the specific color ck. 
         *      - The sum can at most be equal to 1.0 (of course, this is controlled by the normalization from first step)
         *    The P(ck) for each color ck, must be updated after all superpixels has obtained an updated list of their
         *    palette probabilities. 
         * 
         */
        public void AssociateSuperpixelsToPaletteColor()
        {
            // 1. Each superpixel obtains a new updated mapping of palette probabilities
            for (int r = 0; r < Context.OutputSize.Height; r++)
            {
                for(int c = 0; c < Context.OutputSize.Width; c++)
                {
                    Dictionary<int, double> probabilitiesForSp = 
                        GetPaletteConditionalProbability(Context.Superpixels[r][c], Context.Temperature);

                    Context.Superpixels[r][c].PaletteProbabilityMap = probabilitiesForSp;

                    NormalizeProbabilities(Context.Superpixels[r][c]);
                }
            }

            // 2. P(ck) is updated. See the formula in paper for P(ck) = Summation(...)
            foreach (var k in Context.Palette.Keys)
            {
                Context.Palette[k].Probability = 0;
                for (int r = 0; r < Context.OutputSize.Height; r++)
                {
                    for (int c = 0; c < Context.OutputSize.Width; c++)
                    {
                        Context.Palette[k].Probability += (Context.Superpixels[r][c].PaletteProbabilityMap[k] * Context.Superpixels[r][c].Probability);
                    }
                }
            }

            // Assert that there are no NaN for probability. If so, then set converged flag and exit.
            foreach (var k in Context.Palette.Keys)
            {
                if (Double.IsNaN(Context.Palette[k].Probability))
                {
                    Logger.Log("Processor::AssociateSuperpixelsToPaletteColor::NaNProbability=Warning: NaN probability found.");
                    Logger.Log("Processor::AssociateSuperpixelsToPaletteColor::NaNProbability=Terminating at next iteration...");
                    Context.HasConverged = true;
                }
            }
        }

        /**
         * For each color in the palette, ck, loop through all cluster pixels, and then
         * We then begin the process of summing the product of:
         *  - The modified superpixel color (through bilateral filtering in refine step) and 
         *  - the conditional probabiility for which the color ck can be associated for that superpixel
         *  
         * Divide that by P(ck), and we will obtain the new ck for the palette. 
         */
        public double RefinePalette()
        {
            double totalDelta = 0.0;

            // set this to the average of all super pixel colors, weighted by their probability
            foreach (var k in Context.Palette.Keys)
            {
                var newColor = new LabColor(0, 0, 0);
                for(int r = 0; r < Context.OutputSize.Height; r++)
                {
                    for(int c = 0; c < Context.OutputSize.Width; c++)
                    {
                        var ps = Context.Superpixels[r][c];
                        var ms_prime = ps.Color;

                        // Assuming that the superpixel PaletteProbabilityMap is up to date, it should have
                        // all the keys as the Palette.
                        newColor.L += (ms_prime.L * ps.PaletteProbabilityMap[k] * ps.Probability);
                        newColor.A += (ms_prime.A * ps.PaletteProbabilityMap[k] * ps.Probability);
                        newColor.B += (ms_prime.B * ps.PaletteProbabilityMap[k] * ps.Probability);
                    }
                }

                newColor.L /= Context.Palette[k].Probability;
                newColor.A /= Context.Palette[k].Probability;
                newColor.B /= Context.Palette[k].Probability;

                LabColor oldColor = Context.Palette[k].Color;

                // Update the palette color
                Context.Palette[k].Color = newColor;

                totalDelta += ColorDistance(oldColor, newColor);
            }

            if (Configuration.Debug)
            {
                Logger.Log($"Processor::RefinePalette:totalDelta={{value: {totalDelta}}}");
            }

            return totalDelta;
        }

        /**
         * The palette is expanded by splitting existing colors. 
         * Loop through existing colors in the palette and check to see if the color can be split. 
         *  - Check to see if the distance between sub-clusters of each color ck exceeds some value epsilon_c, 
         *  - If it does, then each sub-cluster is added to the palette as an actual (new) palette color. 
         *  - Each new color takes on equal probability and other values
         *  - The original color is removed. 
         */
        public void ExpandPalette()
        {
            if (!CanExpandPalette())
            {
                return;
            }

            var PaletteKeys = new List<int>(Context.Palette.Keys);

            foreach(var k in PaletteKeys)
            {
                if (!Context.PaletteClusters.ContainsKey(k))
                {
                    continue;
                }

                var color1Key = Context.PaletteClusters[k].First;
                var color2Key = Context.PaletteClusters[k].Second;
                if (!Context.Palette.ContainsKey(color1Key) || !Context.Palette.ContainsKey(color2Key))
                {
                    continue;
                }

                var color1 = Context.Palette[color1Key];
                var color2 = Context.Palette[color2Key];

                // Must be greater than cluster error
                double colorError = ColorDistance(color1.Color, color2.Color);
                if (Configuration.Debug)
                {
                    Logger.Log($"Processor::ExpandPalette::colorError={colorError}");
                }

                if (colorError > Context.EpsilonC)
                {
                    // We know we have to create a new color, so increment total number of colors
                    Context.K++;

                    RemoveFromPalette(k);
                    var firstKey = AddToPalette(new PaletteColor(color1.Color, color1.Probability / 2));
                    var secondKey = AddToPalette(new PaletteColor(color2.Color, color2.Probability / 2));

                    var oldCluster = Context.PaletteClusters[k];
                    Context.PaletteClusters.Remove(oldCluster.First);

                    if (oldCluster.Length == 2)
                    {
                        Context.PaletteClusters.Remove(oldCluster.Second);
                    }

                    // Reassociate cluster items at ck with the new colors
                    // We need to fix this one here
                    Context.PaletteClusters.Add(secondKey, new PaletteCluster(secondKey, firstKey));
                    Context.PaletteClusters.Add(firstKey, new PaletteCluster(firstKey, secondKey));
                }
            }

            // Reached max colors. So condense the palette.
            if (Context.K > Context.KMax)
            {
                if (Configuration.Debug)
                {
                    Logger.Log($"Processor::ExpandPalette::Condense=Condensing palette with {Context.K} colors to fit total allowed colors.");
                }

                var clusterKeys = new List<int>(Context.PaletteClusters.Keys);
                foreach (var k in clusterKeys)
                {
                    var cluster = Context.PaletteClusters[k];

                    if (cluster.Length == 2)
                    {
                        if (!Context.Palette.ContainsKey(cluster.First) || !Context.Palette.ContainsKey(cluster.Second))
                        {
                            continue;
                        }

                        if (Configuration.Debug)
                        {
                            Logger.Log($"Processor::ExpandPalette::Condense=Condensing subcluster {cluster.First},{cluster.Second}");
                        }

                        var color1 = Context.Palette[cluster.First];
                        var color2 = Context.Palette[cluster.Second];

                        var newL = (color1.Color.L + color2.Color.L) / 2;
                        var newA = (color1.Color.A + color2.Color.A) / 2;
                        var newB = (color1.Color.B + color2.Color.B) / 2;

                        var newKey  = AddToPalette(new PaletteColor(new LabColor(newL, newA, newB), color1.Probability + color2.Probability));

                        Context.PaletteClusters.Add(newKey, new PaletteCluster(newKey));

                        Context.Palette.Remove(cluster.First);
                        Context.Palette.Remove(cluster.Second);
                        Context.PaletteClusters.Remove(k);
                    }
                }
            } 
            else
            {
                // Update the deltas to perturb colors.
                PcaDelta = FindPcaDeltas();

                // Perturb so sub-clusters can be separated in next iterations
                foreach(var k in Context.PaletteClusters.Keys)
                {
                    if (Context.PaletteClusters[k].Length < 2)
                    {
                        continue;
                    }
                    if (!Context.Palette.ContainsKey(Context.PaletteClusters[k].Second))
                    {
                        continue;
                    }

                    PerturbColor(Context.Palette[Context.PaletteClusters[k].Second]);
                }
            }
        }

        /////////////////////////////////////////////////////////////////////////////////////
        /// *** STABLE *** 
        /// PUBLIC API
        /////////////////////////////////////////////////////////////////////////////////////

        public bool CanExpandPalette()
        {
            return Context.K < Context.KMax;
        }

        public void ReduceTemperature()
        {
            double alpha = 0.7;

            if (Configuration.Debug)
            {
                Logger.Log($"Processor::ReduceTemperature=Reducing temperature with alpha value {alpha} and temperature {Context.Temperature}.");
            }

            Context.Temperature = alpha * Context.Temperature;
        }

        public Image<Bgr, byte> IntermediateProcess(string filename)
        {
            // Assign the pixel colors at the appropriate location
            int ratio = (int)Math.Floor((double)(Context.Input.Width / Context.OutputSize.Width));

            Image<Lab, float> im = new Image<Lab, float>(new System.Drawing.Size(ratio * Context.OutputSize.Width, ratio * Context.OutputSize.Height));

            var data = im.Data;
            for (int rr = 0; rr < ratio * Context.OutputSize.Height; rr++)
            {
                for (int cc = 0; cc < ratio * Context.OutputSize.Width; cc++)
                {
                    int paletteKey = Context.Superpixels[rr / ratio][cc / ratio].PaletteColorKey;

                    LabColor color = Context.Palette[paletteKey].Color;
                    data[rr, cc, 0] = (float)(color.L);
                    data[rr, cc, 1] = (float)(color.A);
                    data[rr, cc, 2] = (float)(color.B);
                }
            }

            // Note to self: It is easier to saturate the image in HSV rather than Lab. 
            // Mostly because I can't get good saturation using the beta=1.1 saturation factor
            // as mentioned in the Gerstner paper.
            Image<Hsv, byte> hsvOut = ToSaturatedHsv(im, new Dimension(ratio * Context.OutputSize.Height, ratio * Context.OutputSize.Width));

            // Convert the HSV image to a BGR image for output.
            Image<Bgr, byte> outputBgr = hsvOut.Convert<Bgr, byte>();
           
            if (Configuration.Debug)
            {
                var suffix = filename.Split(".")[0];
                System.IO.Directory.CreateDirectory($"intermediates_{suffix}");
                CvInvoke.Imwrite($"intermediates_{suffix}/intermediate_{suffix}_{Context.Iteration}.png", outputBgr);
            }

            return outputBgr;
        }

        public void PostProcess(string filename)
        {
            // Make sure all the superpixels have the correct keys by reassociating once more
            AssociateSuperpixelsToPaletteColor();

            // Write the "big" image.
            Image<Bgr, byte> outputBgr = IntermediateProcess(filename);
            CvInvoke.Imwrite($"o_{filename}", outputBgr);

            // Now for the "small image"
            Image<Lab, float> imSmall = new Image<Lab, float>(new System.Drawing.Size(Context.OutputSize.Width, Context.OutputSize.Height));
            var data = imSmall.Data;
            for(int r = 0; r < Context.OutputSize.Height; r++)
            {
                for(int c = 0; c < Context.OutputSize.Width; c++)
                {
                    int paletteKey = Context.Superpixels[r][c].PaletteColorKey;

                    LabColor color = Context.Palette[paletteKey].Color;
                    data[r, c, 0] = (float)(color.L);
                    data[r, c, 1] = (float)(color.A);
                    data[r, c, 2] = (float)(color.B);
                }
            }

            // Saturate
            Image<Hsv, byte> hsvOut = ToSaturatedHsv(imSmall, Context.OutputSize);

            // Convert the HSV image to a BGR image for output.
            Image<Bgr, byte> outputBgrSmall = hsvOut.Convert<Bgr, byte>();
            CvInvoke.Imwrite($"o_small_{filename}", outputBgrSmall);


            if (Configuration.Debug)
            {
                Logger.Log("Processor::PostProcess=Generated image!");
                // CvInvoke.Imshow("All done!", outputBgr);
            }
        }

        /////////////////////////////////////////////////////////////////////////////////////
        /// *** STABLE *** 
        /// PRIVATE UTILITY METHODS
        /////////////////////////////////////////////////////////////////////////////////////

        private int AddToPalette(PaletteColor paletteColor)
        {
            Context.Palette.Add(LatestPaletteKey, paletteColor);
            LatestPaletteKey++;

            // Return the Key used
            return LatestPaletteKey - 1;
        }

        private void RemoveFromPalette(int k)
        {
            Context.Palette.Remove(k);
        }

        private double GaussianFunc(double x, double sigma, double mean)
        {
            double a = 1 / (sigma * Math.Sqrt(2 * Math.PI));
            double p = Math.Pow(x - mean, 2) / (-2.0 * Math.Pow(sigma, 2));

            return a * Math.Exp(p);
        }

        private PrincipalComponentAnalysis FindPca()
        {
            double[][] v = new double[Context.OutputSize.Width * Context.OutputSize.Height][];
            for (int i = 0; i < Context.OutputSize.Width * Context.OutputSize.Height; i++)
            {
                var r = i / Context.OutputSize.Width;
                var c = i % Context.OutputSize.Width;
                var data = Context.Superpixels[r][c].Color;

                v[i] = new double[3] {
                        data.L,
                        data.A,
                        data.B
                    };
            }

            PrincipalComponentAnalysis pca = new PrincipalComponentAnalysis(PrincipalComponentMethod.Center);
            pca.Learn(v);

            return pca;
        }


        private void NormalizeProbabilities(Superpixel ps)
        {
            double sum = ps.PaletteProbabilityMap.Values.Sum();

            if (sum == 0)
            {
                return;
            }

            double maxProbability = ps.PaletteProbabilityMap.Values.Max();

            foreach(var k in ps.PaletteProbabilityMap.Keys)
            {
                if (ps.PaletteProbabilityMap[k] == maxProbability)
                {
                    ps.PaletteColorKey = k;
                }
                ps.PaletteProbabilityMap[k] /= sum;
            }
        }

        /**
         * Obtains the mean color from the assigned pixels of the superpixel.
         */
        private LabColor GetSuperpixelMeanColorFromAssignedPixels(Superpixel ps)
        {
            double sumL = 0;
            double sumA = 0;
            double sumB = 0;
            double length = ps.Pixels.Count;

            var data = Context.Input.Data;
            foreach (var loc in ps.Pixels)
            {
                sumL += data[loc.Row, loc.Column, 0];
                sumA += data[loc.Row, loc.Column, 1];
                sumB += data[loc.Row, loc.Column, 2];
            }

            return new LabColor(sumL / length, sumA / length, sumB / length);
        }

        private bool IsLocationInGrid(int row, int column)
        {
            return row >= 0 && row < Context.OutputSize.Height && column >= 0 && column < Context.OutputSize.Width;
        }

        /**
         * Uses the PCA from Superpixels to compute the temperature to initialize with.
         */
        private double FindCriticalTemperature()
        {
            var pca = FindPca();

            double result;
            try
            {
                result = Math.Abs(pca.Eigenvalues[0]) / 100;

                if (result < 10)
                {
                    result *= 10;
                }
            }
            catch
            {
                result = 100;
            }

            var initialTemperature = 1.1 * result;

            if (Configuration.Debug)
            {
                Logger.Log($"Processor::FindCriticalTemperature::CriticalTemperature={initialTemperature}");
            }

            return initialTemperature;
        }

        /**
         * Given the current palette colors, find the conditional probability at which each palette color is likely to be associated with the 
         * current pixel at the current system temperature.
         */
        private Dictionary<int, double> GetPaletteConditionalProbability(Superpixel superpixel, double temperature)
        {
            Dictionary<int, double> result = new Dictionary<int, double>();
            LabColor ms = superpixel.Color;
            double denominator = 0.0;
            foreach (var c in Context.Palette.Values)
            {
                var cj = c.Color;
                var cdist = ColorDistance(ms, cj);
                denominator += (c.Probability * Math.Pow(Math.E, -1 * cdist / temperature));
            }

            foreach(var k in Context.Palette.Keys)
            {
                var pcolor = Context.Palette[k];
                var cdist = ColorDistance(ms, pcolor.Color);
                var conditionalProbability = pcolor.Probability * Math.Pow(Math.E, -1 * cdist / temperature);

                var pCkPs = conditionalProbability / denominator;

                if (Configuration.Debug)
                {
                    if (Double.IsNaN(pCkPs))
                    {
                        Logger.Log($"Processor::GetPaletteConditionalProbability=Received NaN for PaletteConditionalProbability Calculation.");
                    }
                }


                result.Add(k, pCkPs);
            }

            return result;
        }

        /**
         * Get the delta values required to perturb the color comopnents in Lab space.
         */
        private double[] FindPcaDeltas()
        {
            var pca = FindPca();

            double[] deltas = pca.ComponentVectors[0];

            if (Configuration.Debug)
            {
                Logger.Log($"Processor::FindPcaDeltas::PCA={{[{deltas[0]}, {deltas[1]}, {deltas[2]}]}}");
            }

            double[] weightedDeltas = new double[deltas.Length];
            for (int i = 0; i < 3; i++)
            {
                weightedDeltas[i] = Math.Abs(deltas[i]);
            }

            if (Configuration.Debug)
            {
                Logger.Log($"Processor::FindPcaDeltas::PerturbDeltas={{[{weightedDeltas[0]}, {weightedDeltas[1]}, {weightedDeltas[2]}]}}");
            }

            return weightedDeltas;
        }

        /**
         * Helper to perturb colors so that two colors in a cluster do not end up being the same.
         */
        private PaletteColor PerturbColor(PaletteColor ck)
        {
            ck.Color.L = ck.Color.L + PcaDelta[1];
            ck.Color.A = ck.Color.A + PcaDelta[1];
            ck.Color.B = ck.Color.B + PcaDelta[1];

            return ck;
        }

        /**
         * Finds the mean color in Lab space from the entire input image.
         */
        private static PaletteColor GetInitialMeanColor(Image<Lab, double> input)
        {
            var data = input.Data;
            double sumL = 0;
            double sumA = 0;
            double sumB = 0;

            for (int r = 0; r < input.Height; r++)
            {
                for (int c = 0; c < input.Width; c++)
                {
                    sumL += data[r, c, 0];
                    sumA += data[r, c, 1];
                    sumB += data[r, c, 2];
                }
            }

            var N = input.Height * input.Width;
            double avgL = sumL / N;
            double avgA = sumA / N;
            double avgB = sumB / N;

            PaletteColor meanColor = new PaletteColor(new LabColor(avgL, avgA, avgB), 1);

            return meanColor;
        }

        /**
         * Computes the cost of a pixel being associated with the desired superpixel.
         * The cost is computed at 5 dimensions: 2 positional (x, y), and 3 color (L, A, B)
         * 
         * The weight value is to emphasize the cost more towards the positional difference between
         * the superpixel and pixel locations. 
         * 
         * The superpixel grid starts out as a regular grid, so the pixels which are reassociated to 
         * other superpixels shouldn't really be too far off from its original location.
         */
        private static double GetAssociatedSuperpixelCost(Superpixel ps, Dimension outSize, Image<Lab, double> input, PixelLocation pixelLoc, PixelLocation spLoc)
        {
            var data = input.Data;
            // color distance
            double dc = Math.Sqrt(
                Math.Pow(ps.Color.L - data[pixelLoc.Row, pixelLoc.Column, 0], 2) +
                Math.Pow(ps.Color.A - data[pixelLoc.Row, pixelLoc.Column, 1], 2) +
                Math.Pow(ps.Color.B - data[pixelLoc.Row, pixelLoc.Column, 2], 2)
            );

            // spatial distance
            double dp = Math.Sqrt(
                Math.Pow(spLoc.Row - pixelLoc.Row, 2) +
                Math.Pow(spLoc.Column - pixelLoc.Column, 2)
            );

            double weight = Constants.m * Math.Sqrt((outSize.Width * outSize.Height) / (input.Width * input.Height));

            return dc + weight * dp;
        }

        /**
         * Gets the Euclidean distance between two colors in the Lab color space.
         */
        private double ColorDistance(LabColor first, LabColor second)
        {
            double dc = Math.Sqrt(
                Math.Pow(first.L - second.L, 2f) +
                Math.Pow(first.A - second.A, 2f) +
                Math.Pow(first.B - second.B, 2f)
            );

            return dc;
        }

        /**
         * Creates a saturated HSV image from the input image and specified size.
         */
        private Image<Hsv, byte> ToSaturatedHsv(Image<Lab, float> im, Dimension size)
        {
            Image<Hsv, byte> hsvOut = im.Convert<Hsv, byte>();
            var hsvData = hsvOut.Data;
            for (int rr = 0; rr < size.Height; rr++)
            {
                for (int cc = 0; cc < size.Width; cc++)
                {
                    hsvData[rr, cc, 1] = (byte)Math.Floor(hsvData[rr, cc, 1] + 26.0);
                }
            }

            return hsvOut;
        }

        /**
         * Creates a working image for the entire process from some file.
         */
        private static Image<Lab, double> CreateImageFromFilename(string filename)
        {
            return new Image<Bgr, byte>(filename).Convert<Lab, double>();
        }
    }
}
