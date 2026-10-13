using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Nova.Abstractions.Data.Repositories;
using NovaAlarmData = Nova.Abstractions.Data.Models.AlarmData;

namespace Master.Repository
{
    /// <summary>
    /// <see cref="IAlarmLogRepository"/> backed by the V3 data access layer
    /// (IDataAccess). Writes are a no-op because Nova.Alarm's
    /// AlarmDatabase already saves every PLC alarm; reads query the same [Alarms]
    /// table the V3 report used, so alarm history is identical to V3.
    /// </summary>
    public sealed class AlarmLogRepository : IAlarmLogRepository
    {
        private readonly IDataAccess _dataAccess;

        public AlarmLogRepository(IDataAccess dataAccess)
        {
            _dataAccess = dataAccess ?? throw new ArgumentNullException(nameof(dataAccess));
        }

        public Task AppendAsync(NovaAlarmData alarm)
        {
            // Nova.Alarm's AlarmDatabase already saves every alarm; writing here
            // would duplicate every row.
            return Task.CompletedTask;
        }

        /// <summary>
        /// Reads through IDataAccess.AlarmviewQuery — the repository's own alarm-view
        /// query, written for exactly this screen: it takes the shift alongside the
        /// range and filters in SQL, and it ranges on StartTime only. GetAlarms was the
        /// wrong choice here because its WHERE also requires EndTime &lt;= @endTime, and
        /// EndTime is stamped when the alarm is written (on reset), so any alarm that
        /// started inside the window but was reset after it was silently dropped.
        /// </summary>
        public Task<IReadOnlyList<NovaAlarmData>> QueryAsync(DateTime startTime, DateTime endTime, string shift = null)
        {
            // IDataAccess is synchronous and hits SQL Server, so keep it off the UI thread.
            return Task.Run<IReadOnlyList<NovaAlarmData>>(() =>
            {
                // Null/"All" means every shift; AlarmviewQuery drops the filter for both.
                DataTable table = _dataAccess.AlarmviewQuery(shift, startTime, endTime);
                var result = new List<NovaAlarmData>();
                if (table == null)
                    return result;

                foreach (DataRow row in table.Rows)
                {
                    result.Add(new NovaAlarmData
                    {
                        StartTime = GetDateTime(table, row, "StartTime"),
                        EndTime = GetDateTime(table, row, "EndTime"),
                        AlarmCode = GetString(table, row, "AlarmCode"),
                        AlarmDescription = GetString(table, row, "AlarmDescription"),
                        AlarmType = GetString(table, row, "AlarmType"),
                        Shift = GetString(table, row, "Shift"),
                    });
                }

                return result;
            });
        }

        // AlarmviewQuery is SELECT *, so read by column name and tolerate a column that
        // is absent or null rather than throwing on the whole page of history.
        private static string GetString(DataTable table, DataRow row, string column)
        {
            if (!table.Columns.Contains(column) || row.IsNull(column))
                return string.Empty;
            return Convert.ToString(row[column]);
        }

        private static DateTime GetDateTime(DataTable table, DataRow row, string column)
        {
            if (!table.Columns.Contains(column) || row.IsNull(column))
                return DateTime.MinValue;
            return Convert.ToDateTime(row[column]);
        }
    }
}
