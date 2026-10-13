using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nova.Abstractions.Data;
using Nova.Abstractions.Data.Repositories;

namespace Nova.Core.Services
{
    /// <summary>
    /// The Data page's generated unit records (App.config <c>SampleData=true</c>), shaped
    /// like TE010's output (SOP-35401735-R0): a D-SCUD where each carrier holds two
    /// products and goes to one of the two stations, day shift 08:00-20:00 and night
    /// shift after.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deterministic: a unit's outcome is a hash of its slot on a fixed time grid, so
    /// the same window always shows the same units and a chart does not reshuffle on
    /// every query. Nothing is generated after "now".
    /// </para>
    /// <para>
    /// Implements <see cref="ISampleDataSource"/>, so the Data page labels its figures
    /// as sample data.
    /// </para>
    /// </remarks>
    public sealed class SampleProductionRepository : IProductionRepository, ISampleDataSource
    {
        /// <summary>One carrier out every 28 s, alternating stations: about 257 products an hour.</summary>
        private static readonly TimeSpan Slot = TimeSpan.FromSeconds(28);

        private static readonly DateTime Anchor = new DateTime(2026, 1, 1);

        public Task<IReadOnlyList<ProductionUnitRecord>> GetProductionRecordsAsync(DateTime start, DateTime end, string shift = null)
        {
            return Task.Run<IReadOnlyList<ProductionUnitRecord>>(() =>
            {
                List<ProductionUnitRecord> records = Generate(start, end);

                if (!string.IsNullOrWhiteSpace(shift) && !string.Equals(shift, "ALL", StringComparison.OrdinalIgnoreCase))
                    records = records.Where(r => string.Equals(r.Shift, shift, StringComparison.OrdinalIgnoreCase)).ToList();

                return records;
            });
        }

        public Task<ShiftProduction> GetShiftHourlyProductionAsync()
        {
            return Task.Run(() =>
            {
                DateTime now = DateTime.Now;
                DateTime shiftStart = now.Hour >= 8 && now.Hour < 20
                    ? now.Date.AddHours(8)
                    : (now.Hour >= 20 ? now.Date.AddHours(20) : now.Date.AddDays(-1).AddHours(20));

                List<ProductionUnitRecord> records = Generate(shiftStart, now);
                var buckets = new List<HourlyProductionBucket>();

                for (DateTime t = shiftStart; t < now; t = t.AddHours(1))
                {
                    DateTime slotEnd = t.AddHours(1) < now ? t.AddHours(1) : now;
                    List<ProductionUnitRecord> inSlot = records
                        .Where(r => r.ProductionTime >= t && r.ProductionTime < slotEnd)
                        .ToList();

                    buckets.Add(new HourlyProductionBucket
                    {
                        Start = t,
                        End = slotEnd,
                        Label = t.ToString("HH:mm") + "-" + slotEnd.ToString("HH:mm"),
                        Count = inSlot.Count,
                        PassCount = inSlot.Count(r => r.IsPass),
                        FailCount = inSlot.Count(r => !r.IsPass)
                    });
                }

                return new ShiftProduction
                {
                    HasShift = true,
                    ShiftName = ShiftAt(now),
                    WindowStart = shiftStart,
                    WindowEnd = now,
                    Buckets = buckets
                };
            });
        }

        private static List<ProductionUnitRecord> Generate(DateTime start, DateTime end)
        {
            var records = new List<ProductionUnitRecord>();
            DateTime now = DateTime.Now;
            if (end > now)
                end = now;

            if (end <= start)
                return records;

            long first = (long)Math.Ceiling((start - Anchor).Ticks / (double)Slot.Ticks);
            long last = (long)Math.Floor((end - Anchor).Ticks / (double)Slot.Ticks);

            for (long slot = first; slot <= last; slot++)
            {
                DateTime input = Anchor.AddTicks(slot * Slot.Ticks);

                if (IsDown(input))
                    continue;

                // One carrier per slot, alternating between the two stations; both of
                // its products are dispensed on that station.
                string gantry = slot % 2 == 0 ? "Front" : "Rear";
                string carrier = "UC" + ((slot % 360) + 1).ToString("000");

                for (int cavity = 1; cavity <= 2; cavity++)
                {
                    ProductionUnitRecord record = Unit(slot, cavity, input, gantry, carrier);
                    if (record.ProductionTime >= start && record.ProductionTime <= end)
                        records.Add(record);
                }
            }

            return records;
        }

        /// <summary>
        /// The stops in a window, oldest first: each shift change (08:00 and 20:00, a
        /// quarter of an hour) and each hour's alarm stop. The sample alarm log uses
        /// these, so the sample alarm list and production agree.
        /// </summary>
        internal static IEnumerable<SampleStop> Stops(DateTime from, DateTime to)
        {
            DateTime hour = new DateTime(from.Year, from.Month, from.Day, from.Hour, 0, 0);
            for (; hour <= to; hour = hour.AddHours(1))
            {
                long hourKey = (long)(hour - Anchor).TotalHours;

                if (hour.Hour == 8 || hour.Hour == 20)
                    yield return new SampleStop(hour, hour.AddMinutes(15), true, hourKey);

                double stopShare = Noise(hourKey, 11);
                int stopStart = (int)(Noise(hourKey, 12) * 50);
                int stopLength = stopShare < 0.6 ? (int)(stopShare * 8) : (int)(stopShare * 18);

                if (stopLength > 0)
                {
                    DateTime begin = hour.AddMinutes(Math.Max(stopStart, hour.Hour == 8 || hour.Hour == 20 ? 15 : 0));
                    DateTime end = hour.AddMinutes(stopStart + stopLength);
                    if (end > begin)
                        yield return new SampleStop(begin, end, false, hourKey);
                }
            }
        }

        /// <summary>
        /// Each hour loses a few minutes to an alarm or a maintenance stop, and the
        /// shift changes at 08:00 and 20:00 take a quarter of an hour.
        /// </summary>
        internal static bool IsDown(DateTime time)
        {
            if ((time.Hour == 8 || time.Hour == 20) && time.Minute < 15)
                return true;

            long hourKey = (long)(time - Anchor).TotalHours;
            double stopShare = Noise(hourKey, 11);
            int stopStart = (int)(Noise(hourKey, 12) * 50);
            int stopLength = stopShare < 0.6 ? (int)(stopShare * 8) : (int)(stopShare * 18);

            return time.Minute >= stopStart && time.Minute < stopStart + stopLength;
        }

        private static ProductionUnitRecord Unit(long slot, int cavity, DateTime input, string gantry, string carrier)
        {
            string shift = ShiftAt(input);
            bool night = shift == "NS";

            double roll = Noise(slot * 2 + cavity, 1);
            double preCheckRate = night ? 0.013 : 0.009;
            double aoiRate = night ? 0.009 : 0.006;

            var record = new ProductionUnitRecord
            {
                Date = night && input.Hour < 8 ? input.Date.AddDays(-1) : input.Date,
                Shift = shift,
                // A little of the unit's time goes on the second product.
                ProductionTime = input.AddSeconds(cavity == 2 ? 2.5 : 0),
                ProductCode = "FVM" + input.ToString("yyMMdd") + ((slot % 1000000)).ToString("000000") + cavity,
                ComponentSn = carrier,
                Gantry = gantry,
                Iteration = Noise(slot * 2 + cavity, 7) < 0.005 ? 2 : 1,
                HiveState = Noise(slot * 2 + cavity, 8) < 0.0015 ? 5 : 1
            };

            if (roll < preCheckRate)
            {
                // Thrown out by the pre-process CCD check before any glue went on.
                record.IsPass = false;
                record.TossingCount = 1;
                record.TossingReason = Noise(slot, 9) < 0.8 ? "Pre-check CCD NG" : "Tossing limit exceeded";
                record.FailureCode = record.TossingReason == "Pre-check CCD NG"
                    ? "Tossing-PreCheckNG"
                    : "Tossing Limit Exceeded";
                record.CycleTimeSeconds = 14 + Noise(slot * 2 + cavity, 2) * 3;
            }
            else if (roll < preCheckRate + aoiRate)
            {
                string[] defects = { "GlueMissing", "GlueShift", "GlueBroken", "LessGlue" };
                string defect = defects[(int)(Noise(slot, 10) * defects.Length) % defects.Length];
                record.IsPass = false;
                record.FailureCode = gantry + "-P" + cavity + "-" + defect;
                record.CycleTimeSeconds = 55 + Noise(slot * 2 + cavity, 3) * 6;
            }
            else
            {
                record.IsPass = true;
                record.FailureCode = "NA";
                record.CycleTimeSeconds = 52 + Noise(slot * 2 + cavity, 4) * 7;
            }

            return record;
        }

        private static string ShiftAt(DateTime time)
        {
            return time.Hour >= 8 && time.Hour < 20 ? "DS" : "NS";
        }

        internal static string ShiftAtTime(DateTime time)
        {
            return ShiftAt(time);
        }

        /// <summary>A repeatable number in [0, 1) for a key and a salt.</summary>
        internal static double Noise(long key, int salt)
        {
            unchecked
            {
                ulong x = (ulong)key * 0x9E3779B97F4A7C15UL + (ulong)salt * 0xBF58476D1CE4E5B9UL;
                x ^= x >> 30;
                x *= 0xBF58476D1CE4E5B9UL;
                x ^= x >> 27;
                x *= 0x94D049BB133111EBUL;
                x ^= x >> 31;
                return (x >> 11) * (1.0 / (1UL << 53));
            }
        }

        /// <summary>A repeatable number in [0, 1) for a text key, e.g. a parameter name.</summary>
        internal static double Noise(string key, long bucket)
        {
            // FNV-1a: string.GetHashCode is not guaranteed to be the same between runs.
            unchecked
            {
                ulong hash = 14695981039346656037UL;
                foreach (char c in key ?? string.Empty)
                {
                    hash ^= c;
                    hash *= 1099511628211UL;
                }

                return Noise((long)hash ^ bucket, 41);
            }
        }
    }

    /// <summary>One stop in the sample production day.</summary>
    internal sealed class SampleStop
    {
        public SampleStop(DateTime start, DateTime end, bool isShiftChange, long key)
        {
            Start = start;
            End = end;
            IsShiftChange = isShiftChange;
            Key = key;
        }

        public DateTime Start { get; }

        public DateTime End { get; }

        /// <summary>The 08:00 / 20:00 hand-over rather than an alarm.</summary>
        public bool IsShiftChange { get; }

        /// <summary>A number to derive the stop's reason from, the same on every query.</summary>
        public long Key { get; }
    }
}
