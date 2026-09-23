using System;
using System.Collections.Generic;
using System.Data.SqlClient;

namespace Nova.Alarm
{
    /// <summary>
    /// Saves alarms to the [Alarms] table that the Alarm page reads.
    /// </summary>
    /// <remarks>
    /// The row is written when the alarm starts, with EndTime left empty (the Alarm page
    /// shows that as "Ongoing"), and EndTime is filled in when the alarm ends. So an
    /// alarm is not lost if the HMI is closed while the alarm is active.
    /// </remarks>
    public sealed class AlarmDatabase
    {
        private readonly string _connectionString;
        private readonly Func<string> _getShiftName;

        /// <param name="connectionString">The TEAL_Master connection string from App.config.</param>
        /// <param name="getShiftName">Returns the name of the shift running now.</param>
        public AlarmDatabase(string connectionString, Func<string> getShiftName)
        {
            _connectionString = connectionString;
            _getShiftName = getShiftName;
        }

        /// <summary>Saves a new alarm (EndTime empty) and remembers its row ID.</summary>
        public void SaveStart(AlarmRecord alarm)
        {
            alarm.DatabaseId = Insert(alarm);
        }

        /// <summary>Fills in the EndTime of the alarm's row.</summary>
        public void SaveEnd(AlarmRecord alarm)
        {
            // The start was never saved (e.g. the database was down then): save the whole alarm now.
            if (alarm.DatabaseId == 0)
            {
                alarm.DatabaseId = Insert(alarm);
                return;
            }

            const string sql = "UPDATE [dbo].[Alarms] SET [EndTime] = @EndTime WHERE [ID] = @ID";

            using (var connection = new SqlConnection(_connectionString))
            using (var command = new SqlCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@EndTime", alarm.EndTime.Value);
                command.Parameters.AddWithValue("@ID", alarm.DatabaseId);

                connection.Open();
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Alarms that were still active when the HMI last closed (rows with no EndTime).
        /// </summary>
        public List<AlarmRecord> LoadOpenAlarms()
        {
            const string sql = @"
                SELECT [ID], [AlarmCode], [AlarmType], [AlarmDescription], [StartTime]
                FROM [dbo].[Alarms]
                WHERE [EndTime] IS NULL AND [StartTime] IS NOT NULL";

            var alarms = new List<AlarmRecord>();

            using (var connection = new SqlConnection(_connectionString))
            using (var command = new SqlCommand(sql, connection))
            {
                connection.Open();
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        alarms.Add(new AlarmRecord
                        {
                            DatabaseId = (int)reader["ID"],
                            ErrorCode = reader["AlarmCode"] as string ?? "",
                            ErrorType = reader["AlarmType"] as string ?? "",
                            ErrorDetail = reader["AlarmDescription"] as string ?? "",
                            StartTime = (DateTime)reader["StartTime"],
                        });
                    }
                }
            }

            return alarms;
        }

        /// <summary>Inserts one alarm row and returns its new ID.</summary>
        private int Insert(AlarmRecord alarm)
        {
            const string sql = @"
                INSERT INTO [dbo].[Alarms] ([AlarmCode], [AlarmType], [AlarmDescription], [Shift], [StartTime], [EndTime])
                VALUES (@AlarmCode, @AlarmType, @AlarmDescription, @Shift, @StartTime, @EndTime);
                SELECT CAST(SCOPE_IDENTITY() AS INT);";

            object endTime = alarm.EndTime.HasValue ? (object)alarm.EndTime.Value : DBNull.Value;

            using (var connection = new SqlConnection(_connectionString))
            using (var command = new SqlCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@AlarmCode", alarm.ErrorCode);
                command.Parameters.AddWithValue("@AlarmType", alarm.ErrorType);
                command.Parameters.AddWithValue("@AlarmDescription", alarm.ErrorDetail);
                command.Parameters.AddWithValue("@Shift", _getShiftName() ?? "");
                command.Parameters.AddWithValue("@StartTime", alarm.StartTime);
                command.Parameters.AddWithValue("@EndTime", endTime);

                connection.Open();
                return (int)command.ExecuteScalar();
            }
        }
    }
}
