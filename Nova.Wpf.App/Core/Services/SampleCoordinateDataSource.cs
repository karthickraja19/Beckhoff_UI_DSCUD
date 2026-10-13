using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nova.Abstractions.Data;
using Nova.Abstractions.Services;

namespace Nova.Core.Services
{
    /// <summary>
    /// The Data page's generated coordinates (App.config <c>SampleData=true</c>): the
    /// dispensing start point and the glue path corners of each unit, measured on X, Y and
    /// Z around their nominal, so the Axis tab (S2.4.4.6) can be reviewed. Labelled as
    /// sample data.
    /// </summary>
    public sealed class SampleCoordinateDataSource : ICoordinateDataSource, ISampleDataSource
    {
        /// <summary>One unit every 28 s, as the sample production.</summary>
        private static readonly TimeSpan Slot = TimeSpan.FromSeconds(28);

        private static readonly DateTime Anchor = new DateTime(2026, 1, 1);

        /// <summary>Point, axis, nominal (mm).</summary>
        private static readonly Tuple<string, string, double>[] Points =
        {
            Tuple.Create("1", "X", 140.000), Tuple.Create("1", "Y", 85.000), Tuple.Create("1", "Z", 12.500),
            Tuple.Create("2", "X", 152.400), Tuple.Create("2", "Y", 85.000), Tuple.Create("2", "Z", 12.500),
            Tuple.Create("3", "X", 152.400), Tuple.Create("3", "Y", 97.200), Tuple.Create("3", "Z", 12.500),
            Tuple.Create("4", "X", 140.000), Tuple.Create("4", "Y", 97.200), Tuple.Create("4", "Z", 12.500),
        };

        private const double Tolerance = 0.100;

        public Task<IReadOnlyList<CoordinateRecord>> QueryAsync(DateTime start, DateTime end)
        {
            var records = new List<CoordinateRecord>();
            if (end <= start)
                return Task.FromResult<IReadOnlyList<CoordinateRecord>>(records);

            // At most the latest 2000 units, so a week-long window stays quick.
            long first = Math.Max((long)Math.Ceiling((start - Anchor).Ticks / (double)Slot.Ticks),
                                  (long)((end - Anchor).Ticks / Slot.Ticks) - 2000);
            long last = (long)((end - Anchor).Ticks / Slot.Ticks);

            for (long unit = first; unit <= last; unit++)
            {
                DateTime time = Anchor + TimeSpan.FromTicks(Slot.Ticks * unit);
                if (time < start || time > end || time > DateTime.Now)
                    continue;

                string sn = "TE010" + (unit % 100000000).ToString("00000000");
                for (int p = 0; p < Points.Length; p++)
                {
                    Tuple<string, string, double> point = Points[p];

                    // About a 20 um spread, with a rare excursion past the limits.
                    double noise = Gaussian(unit, p) * 0.020;
                    if (SampleProductionRepository.Noise(unit, 700 + p) < 0.004)
                        noise += 0.12 * (SampleProductionRepository.Noise(unit, 800 + p) < 0.5 ? -1 : 1);

                    records.Add(new CoordinateRecord
                    {
                        Time = time,
                        UnitSn = sn,
                        Point = point.Item1,
                        Axis = point.Item2,
                        Value = Math.Round(point.Item3 + noise, 3),
                        Nominal = point.Item3,
                        Lsl = point.Item3 - Tolerance,
                        Usl = point.Item3 + Tolerance,
                    });
                }
            }

            return Task.FromResult<IReadOnlyList<CoordinateRecord>>(records);
        }

        /// <summary>A repeatable standard-normal number (Box-Muller).</summary>
        private static double Gaussian(long unit, int point)
        {
            double u1 = Math.Max(1e-12, SampleProductionRepository.Noise(unit, 500 + point));
            double u2 = SampleProductionRepository.Noise(unit, 600 + point);
            return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }
    }
}
