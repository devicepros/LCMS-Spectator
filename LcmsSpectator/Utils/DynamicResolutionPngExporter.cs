﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="DynamicResolutionPngExporter.cs" company="Pacific Northwest National Laboratory">
//   2015 Pacific Northwest National Laboratory
// </copyright>
// <author>Christopher Wilkins</author>
// <summary>
//   Exporter for PNG files with dynamic resolution.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

using System;
using OxyPlot;
using OxyPlot.Wpf;

namespace LcmsSpectator.Utils
{
    /// <summary>
    /// Exporter for PNG files with dynamic resolution.
    /// </summary>
    public class DynamicResolutionPngExporter
    {
        /// <summary>The export.</summary>
        /// <param name="plotModel">The plot model to export.</param>
        /// <param name="filePath">The output file path.</param>
        /// <param name="suggestedWidth">The suggested width.</param>
        /// <param name="suggestedHeight">The suggested height.</param>
        /// <param name="backgroundColor">The background color.</param>
        /// <param name="dpi">The DPI resolution.</param>
        public static void Export(
                                    PlotModel plotModel,
                                    string filePath,
                                    int suggestedWidth,
                                    int suggestedHeight,
                                    OxyColor backgroundColor,
                                    int dpi)
        {
            var upscaleFactor = dpi / 96.0;
            var width = (int)Math.Ceiling(suggestedWidth * upscaleFactor);
            var height = (int)Math.Ceiling(suggestedHeight * upscaleFactor);

            PngExporter.Export(plotModel, filePath, width, height, backgroundColor, dpi);
        }
    }
}
