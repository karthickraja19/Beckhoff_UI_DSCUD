using System;
using System.Collections.Generic;
using System.Linq;
using Nova.Abstractions.Data.Repositories;

namespace Master.Repository
{
    /// <summary>
    /// <see cref="IAlarmHistoryRepository"/> over IDataAccess: the [Alarms] rows Nova.Alarm
    /// saves as PLC alarms start and end (InsertAlarm, UpdateAlarmEndTime, GetOpenAlarms).
    /// </summary>
    public sealed class AlarmHistoryRepository : IAlarmHistoryRepository
    {
        private readonly IDataAccess _dataAccess;

        public AlarmHistoryRepository(IDataAccess dataAccess)
        {
            _dataAccess = dataAccess ?? throw new ArgumentNullException(nameof(dataAccess));
        }

        public int Insert(string alarmType, string alarmCode, string alarmDescription, DateTime startTime, DateTime? endTime)
        {
            return _dataAccess.InsertAlarm(
                _dataAccess.GetCurrentShift()?.ShiftName ?? string.Empty,
                alarmType,
                alarmCode,
                alarmDescription,
                startTime,
                endTime);
        }

        public void SetEndTime(int alarmId, DateTime endTime)
        {
            _dataAccess.UpdateAlarmEndTime(alarmId, endTime);
        }

        public IReadOnlyList<OpenAlarmRow> GetOpenAlarms()
        {
            return _dataAccess.GetOpenAlarms()
                .Select(row => new OpenAlarmRow
                {
                    Id = row.Id,
                    AlarmCode = row.AlarmCode,
                    AlarmType = row.AlarmType,
                    AlarmDescription = row.AlarmDescription,
                    StartTime = row.StartTime,
                })
                .ToList();
        }
    }
}
