using System;
using System.Collections.Generic;

namespace Nova.Hive
{
    /// <summary>The spread of the captures so far on one axis pair: what is uploaded for one capture.</summary>
    public sealed class RepeatabilityDelta
    {
        public double DeltaX { get; set; }

        public double DeltaY { get; set; }

        /// <summary>How many captures the deltas are over, this one included.</summary>
        public int Captures { get; set; }
    }

    /// <summary>
    /// The delta calculation of a CCD repeatability test (S3.4 PD-13 V3.0, "Delta calculation
    /// logic"), per gantry:
    /// <list type="bullet">
    /// <item>1st capture: only recorded, nothing is uploaded.</item>
    /// <item>2nd capture: delta_x and delta_y = max(1st, 2nd) - min(1st, 2nd).</item>
    /// <item>n-th capture: max - min over all n captures.</item>
    /// </list>
    /// So 100 uploads take 101 captures. A new round (continue, next operator) starts again.
    /// </summary>
    public sealed class RepeatabilityRun
    {
        private readonly Dictionary<string, Extent> _byGantry = new Dictionary<string, Extent>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Takes one capture in. Null for a gantry's first capture; its deltas so far from then on.</summary>
        public RepeatabilityDelta Add(string gantry, double x, double y)
        {
            string key = gantry ?? string.Empty;
            Extent extent;
            if (!_byGantry.TryGetValue(key, out extent))
            {
                _byGantry[key] = new Extent { MinX = x, MaxX = x, MinY = y, MaxY = y, Count = 1 };
                return null;
            }

            extent.MinX = Math.Min(extent.MinX, x);
            extent.MaxX = Math.Max(extent.MaxX, x);
            extent.MinY = Math.Min(extent.MinY, y);
            extent.MaxY = Math.Max(extent.MaxY, y);
            extent.Count++;

            return new RepeatabilityDelta
            {
                DeltaX = extent.MaxX - extent.MinX,
                DeltaY = extent.MaxY - extent.MinY,
                Captures = extent.Count,
            };
        }

        /// <summary>The key of a delta: <c>front_delta_x_test1</c>.</summary>
        public static string Key(string gantry, string axis, int test)
        {
            string prefix = string.IsNullOrWhiteSpace(gantry) ? string.Empty : gantry.Trim().ToLowerInvariant() + "_";
            return prefix + "delta_" + axis + "_test" + test;
        }

        private sealed class Extent
        {
            public double MinX, MaxX, MinY, MaxY;
            public int Count;
        }
    }
}
