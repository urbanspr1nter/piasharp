using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using PiaSharp.Core;
using PiaSharp.Core.Objects;
using PiaSharp.Core.DebugUtils;

namespace PiaSharp.Driver
{
    class Program
    {
        static void Process(Params p, ILogger logger)
        {
            // Turn on debug flag if needed.
            Configuration.Debug = p.Debug;

            Stopwatch sw = new Stopwatch();

            sw.Start();

            if (p.TotalColors > 16)
            {
                logger.Log("Main::Cannot have more than 16 colors. Terminating this job.");
                return;
            }

            // Set up the Inputs, and runtime objects
            string outputFile = p.OutputFile;

            // Step 1. Initialize Superpixels, Palette, and Temperature.
            // The Processor object will internally take care of a lot of things, such as initialization
            logger.Log($"Total Colors: {p.TotalColors}");
            Processor processor =
                new Processor(logger, p.File, new Dimension(p.OutputHeight, p.OutputWidth), p.TotalColors);

            // Step 2. Iterate until temperature is less than final temperature, or we have enough colors.

            // As the current temperature approaches the final temperature, each superpixel is guaranteed to then
            // have high favorability towards a specific palette color. Therefore we can be certain all superpixels 
            // have now been associated with a palette color.

            Context.Iteration = 0;
            Context.HasConverged = false;
            while (!Context.HasConverged)
            {
                if (Configuration.Debug)
                {
                    logger.Log("-------------------");
                    logger.Log($"ITERATION: {Context.Iteration}");
                    logger.Log($"TOTAL COLORS: {processor.K}");
                    logger.Log($"TEMPERATURE: {processor.Temperature}");
                    logger.Log("-------------------");
                }
                else
                {
                    logger.Log($"ITERATION: {Context.Iteration}");
                }

                // Step 2.1 Refine Superpixels with 1 iteration of SLIC
                processor.RefineSuperpixels();

                // Step 2.2 Associate Superpixels to Palette
                processor.AssociateSuperpixelsToPaletteColor();

                if (Configuration.Debug)
                {
                    // We can output intermediate frames for tracking.
                    processor.IntermediateProcess(outputFile);
                }

                // Step 2.3 Refine the Palette -- This effectively creates a new palette in the current context.
                double cEpsilonP = processor.RefinePalette();

                // The change was small, then it is time to expand the palette and include more colors.
                if (cEpsilonP < processor.EpsilonP)
                {
                    // Converged if the current temperature is at, or less than final temperature AND palette has expanded
                    // OR there has been too many iterations.
                    if ((processor.Temperature <= processor.TemperatureFinal && processor.K >= processor.KMax) || 
                            HasExceededIterationSafetyThreshold())
                    {
                        Context.HasConverged = true;
                    } 
                    else
                    {
                        // Set this.
                        Context.PreviousChangeIteration = Context.Iteration;
                        // Step 2.4 Palette has converged as the change in colors is very small.

                        // Reduce the termparture so that now the likelihood of a superpixel belonging to a 
                        // specific palette color increases.
                        // In other words, variance decreases, and superpixels tend to favor certain palette colors over others.
                        processor.ReduceTemperature();
                    }

                    // We continue to expand the palette until we have reached the desired number of palette colors, K.
                    processor.ExpandPalette();
                }

                Context.Iteration++;
            }

            sw.Stop();

            logger.Log($"Complete! Took: {sw.Elapsed.TotalMilliseconds} ms");
            logger.Log("Generating output preview... This may take a while...");

            // Step 3. Post Process, and return the output image
            processor.PostProcess(outputFile);
        }

        static bool HasExceededIterationSafetyThreshold()
        {
            return (Context.Iteration - Context.PreviousChangeIteration) >= Constants.ChangeIterationsThreshold;
        }

        static void Main()
        {
            ConsoleLogger logger = new ConsoleLogger();

            string jsonParams = File.ReadAllText("params.json");
            List<Params> pList = Newtonsoft.Json.JsonConvert.DeserializeObject<List<Params>>(jsonParams);

            foreach(Params p in pList)
            {
                if (p.Skip)
                {
                    logger.Log($"Skipping {p.File}");
                    
                }
                else
                {
                    logger.Log("Working on configuration: " + p.File);
                    Process(p, logger);
                }
            }
        }
    }
}
