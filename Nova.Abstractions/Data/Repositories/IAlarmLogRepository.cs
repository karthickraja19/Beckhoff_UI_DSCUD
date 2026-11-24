using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nova.Abstractions.Data.Models;

namespace Nova.Abstractions.Data.Repositories
{
    /// <summary>
    /// Read side of the persisted alarm log. Rows are written by Nova.Alarm's
    /// AlarmMonitor (through IAlarmHistoryRepository) as PLC alarms start and end, so implementations leave
    /// <see cref="AppendAsync"/> as a no-op rather than double-writing the same row.
    /// </summary>
    public interface IAlarmLogRepository
    {
        Task AppendAsync(AlarmData alarm);

        /// <summary>
        /// History for a range, optionally narrowed to one shift. Pass null for
        /// every shift.
        /// </summary>
        Task<IReadOnlyList<AlarmData>> QueryAsync(DateTime startTime, DateTime endTime, string shift = null);
    }
}
