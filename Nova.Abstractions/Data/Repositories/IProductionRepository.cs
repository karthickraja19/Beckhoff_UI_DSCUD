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
    /// Data page (Audio SW DFM v5.4 slides 59 and 60).
    /// </summary>
    /// <remarks>
    /// The last six properties are the ones a D-SCUD also reports to HIVE with each
    /// unit (slides 15-18). [ProductionData] does not store them yet, so a record read
    /// from it leaves them empty and the page shows "—" rather than a guess.
    /// </remarks>
    public sealed class ProductionUnitRecord
    {
        /// <summary>Production date. A night-shift unit after midnight keeps its shift's date.</summary>
        public DateTime Date { get; set; }
        public string Shift { get; set; }

        /// <summary>When the unit entered the station (HIVE input_time).</summary>
        public DateTime ProductionTime { get; set; }

        /// <summary>Unit SN.</summary>
        public string ProductCode { get; set; }
        public bool IsPass { get; set; }

        /// <summary>output_time - input_time, in seconds (HIVE unit_ct).</summary>
        public double CycleTimeSeconds { get; set; }

        /// <summary>1 for the first pass, higher for a retest of the same unit.</summary>
        public int Iteration { get; set; }

        /// <summary>"Front" or "Rear" on a D-SCUD (slide 16: front is the gantry nearest the input). Null when unknown.</summary>
        public string Gantry { get; set; }

        /// <summary>Linked serials, e.g. the carrier the unit rode on. Null when none are recorded.</summary>
        public string ComponentSn { get; set; }

        /// <summary>HIVE state when the unit was processed (hive_state: 1, 2 or 5). Null when not recorded.</summary>
        public int? HiveState { get; set; }

        /// <summary>
        /// Tossing events on this record: 1 when the unit was thrown out by the
        /// pre-process CCD check (first_vision_tossing_NG = 1), otherwise 0.
        /// </summary>
        public int TossingCount { get; set; }

        /// <summary>Why the unit was tossed, e.g. "Pre-check CCD NG". Null when it was not.</summary>
        public string TossingReason { get; set; }

        /// <summary>HIVE failure_code: "NA" for a pass, otherwise the root failure. Null when not recorded.</summary>
        public string FailureCode { get; set; }

        /// <summary>When the unit left the station (HIVE output_time).</summary>
        public DateTime OutputTime => ProductionTime.AddSeconds(CycleTimeSeconds);
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
