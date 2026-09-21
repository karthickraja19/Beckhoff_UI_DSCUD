using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nova.Abstractions.Data.Repositories
{
    /// <summary>One row of the shift master: the window a named shift runs over.</summary>
    public sealed class ShiftDefinition
    {
        public string ShiftName { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
    }

    /// <summary>
    /// Shift configuration behind Settings → General, matching what V3's Settings form
    /// does through IDataAccess: read the shift master, edit a shift's window, and
    /// record planned downtime / break time for a shift-date.
    /// </summary>
    public interface IShiftSettingsRepository
    {
        /// <summary>Every configured shift, from [ShiftMasterData].</summary>
        Task<IReadOnlyList<ShiftDefinition>> GetShiftsAsync();

        /// <summary>Rewrites one shift's start and end time.</summary>
        Task UpdateShiftAsync(string shiftName, TimeSpan startTime, TimeSpan endTime);

        /// <summary>
        /// Records planned downtime and break time for a shift-date, then recomputes
        /// the <em>current</em> shift's status — V3's update_Click path. Both values feed
        /// the availability denominator behind OEE.
        /// </summary>
        Task UpdatePlannedDowntimeAsync(DateTime shiftDate, string shiftName, int downtimeMinutes, int breakMinutes);

        /// <summary>
        /// Same write, but recomputes the status of the <em>selected</em> shift and date
        /// rather than the current one — V3's recalculate_Click path, for correcting a
        /// shift that has already finished.
        /// </summary>
        Task RecalculateShiftAsync(DateTime shiftDate, string shiftName, int downtimeMinutes, int breakMinutes);
    }
}
