using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nova.Abstractions.Data.Repositories
{
    /// <summary>
    /// One hourly bucket of the UPH chart: the units produced between
    /// <see cref="Start"/> and <see cref="End"/>, with the axis label V3 renders.
    /// </summary>
    public sealed class HourlyProductionBucket
    {
        public DateTime Start { get; set; }
        public DateTime End { get; set; }

        /// <summary>V3's X-axis label format, "HH:mm-HH:mm".</summary>
        public string Label { get; set; }

        /// <summary>Units produced in the slot (V3's UPH series).</summary>
        public int Count { get; set; }

        public int PassCount { get; set; }

        public int FailCount { get; set; }
    }

    /// <summary>
    /// Hourly production for one shift, plus the window it was measured over so the
    /// UI can say which shift it is showing — and can tell "no shift configured"
    /// apart from "shift running, nothing produced yet".
    /// </summary>
    public sealed class ShiftProduction
    {
        /// <summary>False when no usable shift window exists, in which case
        /// <see cref="Buckets"/> is empty. Plotting anything then would be a guess.</summary>
        public bool HasShift { get; set; }

        public string ShiftName { get; set; }

        /// <summary>Shift start.</summary>
        public DateTime WindowStart { get; set; }

        /// <summary>Where plotting stopped: now, or the shift's end if it has passed.</summary>
        public DateTime WindowEnd { get; set; }

        public IReadOnlyList<HourlyProductionBucket> Buckets { get; set; }
            = new HourlyProductionBucket[0];
    }

    /// <summary>
    /// One produced unit, as stored in [ProductionData] — the rows behind the
    /// Reports → Production Data table.
    /// </summary>
    public sealed class ProductionUnitRecord
    {
        public DateTime Date { get; set; }
        public string Shift { get; set; }
        public DateTime ProductionTime { get; set; }
        public string ProductCode { get; set; }
        public bool IsPass { get; set; }
        public double CycleTimeSeconds { get; set; }
        public int Iteration { get; set; }
    }

    /// <summary>
    /// Read side of the production record, for the Home screen's UPH chart and the
    /// Reports → Production Data table. Writes stay with the legacy engine (the
    /// sequences and ShiftStatusHelper).
    /// </summary>
    public interface IProductionRepository
    {
        /// <summary>
        /// Units produced in a date range, optionally narrowed to one shift. Pass
        /// null or "ALL" for every shift.
        /// </summary>
        Task<IReadOnlyList<ProductionUnitRecord>> GetProductionRecordsAsync(
            DateTime start, DateTime end, string shift = null);

        /// <summary>
        /// Hourly production for the current shift only — one bucket per hour from the
        /// shift's start, the last one truncated at the current time (or at the shift's
        /// end once it has passed). Same window V3's
        /// HomeScreen.LoadUPHXYaxisRange() plots.
        /// </summary>
        Task<ShiftProduction> GetShiftHourlyProductionAsync();
    }
}
