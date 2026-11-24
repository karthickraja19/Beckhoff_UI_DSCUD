using System;
using System.Collections.Generic;

namespace Nova.Abstractions.Data.Repositories
{
    /// <summary>
    /// Write side of the persisted alarm log: the [Alarms] rows Nova.Alarm saves as PLC
    /// alarms start and end. Calls are synchronous and may be slow or throw when the
    /// database cannot be reached, so callers run them off the UI and ADS threads.
    /// </summary>
    public interface IAlarmHistoryRepository
    {
        /// <summary>
        /// Saves one alarm, in the shift running now, and returns its row ID. EndTime is
        /// left empty (the Alarm page shows "Ongoing") when <paramref name="endTime"/> is null.
        /// </summary>
        int Insert(string alarmType, string alarmCode, string alarmDescription, DateTime startTime, DateTime? endTime);

        /// <summary>Fills in the EndTime of the alarm's row.</summary>
        void SetEndTime(int alarmId, DateTime endTime);

        /// <summary>Alarms that have no EndTime: still active when the HMI last closed.</summary>
        IReadOnlyList<OpenAlarmRow> GetOpenAlarms();
    }

    /// <summary>One [Alarms] row that has no EndTime yet.</summary>
    public sealed class OpenAlarmRow
    {
        public int Id { get; set; }
        public string AlarmCode { get; set; }
        public string AlarmType { get; set; }
        public string AlarmDescription { get; set; }
        public DateTime StartTime { get; set; }
    }
}
