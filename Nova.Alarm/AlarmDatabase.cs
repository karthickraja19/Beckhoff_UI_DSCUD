using System;
using System.Collections.Generic;
using Master.Repository;

namespace Nova.Alarm
{
    /// <summary>
    /// Saves alarms to the [Alarms] table that the Alarm page reads, through
    /// <see cref="IDataAccess"/>.
    /// </summary>
    /// <remarks>
    /// The row is written when the alarm starts, with EndTime left empty (the Alarm page
    /// shows that as "Ongoing"), and EndTime is filled in when the alarm ends. So an
    /// alarm is not lost if the HMI is closed while the alarm is active.
    /// </remarks>
    public sealed class AlarmDatabase
    {
        private readonly IDataAccess _dataAccess;

        /// <param name="dataAccess">The SQL Server data access of TEAL_Master; it also says which shift is running now.</param>
        public AlarmDatabase(IDataAccess dataAccess)
        {
            _dataAccess = dataAccess ?? throw new ArgumentNullException(nameof(dataAccess));
        }

        /// <summary>Saves a new alarm (EndTime empty) and remembers its row ID.</summary>
        public void SaveStart(AlarmRecord alarm)
        {
            // Always without an end time, even if the alarm has ended by now: the end is
            // saved by SaveEnd, which is queued after this.
            alarm.DatabaseId = Insert(alarm, null);
        }

        /// <summary>Fills in the EndTime of the alarm's row.</summary>
        public void SaveEnd(AlarmRecord alarm)
        {
            // The start was never saved (e.g. the database was down then): save the whole alarm now.
            if (alarm.DatabaseId == 0)
            {
                alarm.DatabaseId = Insert(alarm, alarm.EndTime);
                return;
            }

            _dataAccess.UpdateAlarmEndTime(alarm.DatabaseId, alarm.EndTime.Value);
        }

        /// <summary>
        /// Alarms that were still active when the HMI last closed (rows with no EndTime).
        /// </summary>
        public List<AlarmRecord> LoadOpenAlarms()
        {
            var alarms = new List<AlarmRecord>();

            foreach (OpenAlarm row in _dataAccess.GetOpenAlarms())
            {
                alarms.Add(new AlarmRecord
                {
                    DatabaseId = row.Id,
                    ErrorCode = row.AlarmCode,
                    ErrorType = row.AlarmType,
                    ErrorDetail = row.AlarmDescription,
                    StartTime = row.StartTime,
                });
            }

            return alarms;
        }

        /// <summary>Inserts one alarm row and returns its new ID.</summary>
        private int Insert(AlarmRecord alarm, DateTime? end)
        {
            return _dataAccess.InsertAlarm(
                _dataAccess.GetCurrentShift()?.ShiftName ?? string.Empty,
                alarm.ErrorType,
                alarm.ErrorCode,
                alarm.ErrorDetail,
                alarm.StartTime,
                end);
        }
    }
}
