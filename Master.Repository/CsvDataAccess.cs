using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Master.Repository
{
    public class CsvDataAccess : IDataAccess
    {
        private string _dataAccessPath;

        private static CsvDataAccess instance = null;
        public static CsvDataAccess GetInstance(string dataAccessPath)
        {
            if (instance == null)
            {
                instance = new CsvDataAccess(dataAccessPath);
            }
            return instance;
        }

        public Shift GetCurrentShift()
        {
            throw new NotImplementedException();
        }

        public List<Shift> GetShiftMasterData()
        {
            throw new NotImplementedException();
        }

        public void UpdateShiftMasterData(string shiftName, DateTime startTime, DateTime endTime)
        {
            throw new NotImplementedException();
        }

        public void AddHealthData(HealthData healthRecord)
        {
            throw new NotImplementedException();
        }
        public List<HealthData> GetHealthData(DateTime date, string shift)
        {
            throw new NotImplementedException();
        }

        public void AddProductionData(ProductionData productionData)
        {
            throw new NotImplementedException();
        }

        public ProductionData GetProductionData(string productCode)
        {
            throw new NotImplementedException();
        }

        public void UpdateProductionData(string productCode, bool productionStatus, int iteration)
        {
            throw new NotImplementedException();
        }

        public void AddShiftData(ShiftData shiftData)
        {
            throw new NotImplementedException();
        }

        public void UpdateShiftPlannedDowntime(DateTime date, string shift, int plannedDownTime, int breakTime)
        {
            throw new NotImplementedException();
        }

        public void UpdateShiftData(ShiftData shiftData)
        {
            throw new NotImplementedException();
        }

        public ShiftData GetShiftData(DateTime date, string shift)
        {
            throw new NotImplementedException();
        }

        public void AddOEEData(OEEData oEEData)
        {
            throw new NotImplementedException();
        }

        public List<OEEData> GetOEEData(DateTime StartTime, DateTime endTime)
        {
            throw new NotImplementedException();
        }

        public void AddProductionDataHourly(ProductionDataHourly productionDataHourly)
        {
            throw new NotImplementedException();
        }
        public int GetProductionCountByHour(DateTime startTime, DateTime endTime)
        {
            throw new NotImplementedException();
        }
        public static void AuditLog(string mode, string userName, string action, DateTime actionTime, string logFilePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",",
                mode,
                userName,
                action,
                actionTime));
            if (!File.Exists(logFilePath))
            {
                File.WriteAllText(logFilePath, "Mode,Username,Action,Action Time\n");
            }
            File.AppendAllText(logFilePath, sb.ToString());
        }
        public static void LogCsvChanges(DataTable oldDt, DataTable newDt, string userName, string mode, string fileName, string logFilePath)
        {
            var sb = new StringBuilder();

            for (int i = 0; i < newDt.Rows.Count; i++)
            {
                for (int j = 0; j < newDt.Columns.Count; j++)
                {
                    string oldValue = oldDt.Rows[i][j].ToString();
                    string newValue = newDt.Rows[i][j].ToString();

                    if (oldValue != newValue)
                    {
                        sb.AppendLine(string.Join(",",
                            mode,
                            userName,
                            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                            fileName,
                            i,
                            newDt.Columns[j].ColumnName,
                            oldValue.Replace(",", " "),
                            newValue.Replace(",", " ")
                        ));
                    }
                }
            }

            if (!File.Exists(logFilePath))
            {
                File.WriteAllText(logFilePath,
                    "Mode,Username,DateTime,FileName,RowIndex,ColumnName,OldValue,NewValue\n");
            }

            File.AppendAllText(logFilePath, sb.ToString());
        }

        public List<AlarmData> GetAlarms(DateTime startTime, DateTime endTime)
        {
            throw new NotImplementedException();
        }

        public List<ProductionData> GetProductionData(DateTime StartTime, DateTime EndTime)
        {
            throw new NotImplementedException();
        }
        public void DeleteHealthDataBefore(DateTime Date)
        {
            throw new NotImplementedException();
        }

        public void DeleteProductionDataBefore(DateTime retentionDate)
        {
            throw new NotImplementedException();
        }

        public void DeleteShiftStatusDataBefore(DateTime retentionDate)
        {
            throw new NotImplementedException();
        }

        public void DeleteAlarmDataBefore(DateTime retentionDate)
        {
            throw new NotImplementedException();
        }

        public void DeleteOEEDataBefore(DateTime retentionDate)
        {
            throw new NotImplementedException();
        }

        public CsvDataAccess(string dataAccessPath)
        {
            _dataAccessPath = dataAccessPath;
        }

        // ---- Alarm log ---------------------------------------------------------
        // The CSV stand-in for [TEAL_Master].[dbo].[Alarms]. Column names and order
        // match the table, because AlarmLogRepository reads the DataTable this
        // returns by column name and SQLDataAccess.AlarmviewQuery is a SELECT *.
        // Both alarm methods used to throw NotImplementedException, which surfaced on
        // the Alarm report as "Could not load alarm history: The method or operation
        // is not implemented." every time Load was pressed without SQL configured.

        private const string AlarmLogFileName = "Alarms.csv";

        private static readonly string[] AlarmColumns =
        {
            "StartTime", "EndTime", "Shift", "AlarmType", "AlarmCode", "AlarmDescription",
        };

        /// <summary>Serialises append and read: alarms are written from the alarm
        /// engine's thread while the report screen may be reading.</summary>
        private static readonly object _alarmFileSync = new object();

        private string AlarmLogPath
        {
            get { return Path.Combine(_dataAccessPath ?? string.Empty, AlarmLogFileName); }
        }

        public void InsertAlarmQuery(string _shift, string _smsg, string _command, DateTime _starttime)
        {
            lock (_alarmFileSync)
            {
                string path = AlarmLogPath;
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (!File.Exists(path))
                {
                    File.WriteAllText(path, string.Join(",", AlarmColumns) + Environment.NewLine);
                }

                // AlarmType is "Critical" for the same reason SQLDataAccess hard-codes it:
                // sp_InsertAlarmLogs was always called with that value.
                string line = string.Join(",",
                    CsvField(_starttime.ToString("yyyy-MM-dd HH:mm:ss")),
                    CsvField(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),
                    CsvField(_shift),
                    CsvField("Critical"),
                    CsvField(_command),
                    CsvField(_smsg));

                File.AppendAllText(path, line + Environment.NewLine);
            }
        }

        // Alarms are kept with a row ID and an open EndTime only in SQL Server. In CSV mode
        // the HMI does not save alarms (no connection string), so these are not needed.
        public int InsertAlarm(string shift, string alarmType, string alarmCode, string alarmDescription, DateTime startTime, DateTime? endTime)
        {
            throw new NotImplementedException();
        }

        public void UpdateAlarmEndTime(int alarmId, DateTime endTime)
        {
            throw new NotImplementedException();
        }

        public List<OpenAlarm> GetOpenAlarms()
        {
            throw new NotImplementedException();
        }

        public DataTable AlarmviewQuery(string _shift, DateTime Starttime, DateTime Endtime)
        {
            var table = new DataTable();
            foreach (string column in AlarmColumns)
            {
                table.Columns.Add(column, typeof(string));
            }

            // Same shift semantics as SQLDataAccess: null/empty/"All" means every shift.
            bool allShifts = string.IsNullOrWhiteSpace(_shift) ||
                             _shift.Equals("All", StringComparison.OrdinalIgnoreCase);

            List<string> lines;
            lock (_alarmFileSync)
            {
                string path = AlarmLogPath;
                if (!File.Exists(path))
                {
                    // No alarm has been written yet — an empty history, not an error.
                    return table;
                }

                lines = new List<string>(File.ReadAllLines(path));
            }

            var rows = new List<DataRow>();
            // Row 0 is the header.
            for (int i = 1; i < lines.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    continue;
                }

                string[] fields = SplitCsvLine(lines[i]);
                if (fields.Length < AlarmColumns.Length)
                {
                    // A half-written line (power loss mid-append) must not lose the rest
                    // of the history.
                    continue;
                }

                DateTime startTime;
                if (!DateTime.TryParse(fields[0], System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out startTime))
                {
                    continue;
                }

                // Ranged on StartTime only, matching AlarmviewQuery in SQL: EndTime is
                // stamped when the alarm is written, so ranging on it would drop alarms
                // that started inside the window and were reset after it.
                if (startTime < Starttime || startTime > Endtime)
                {
                    continue;
                }

                if (!allShifts && !string.Equals(fields[2], _shift, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                DataRow row = table.NewRow();
                for (int c = 0; c < AlarmColumns.Length; c++)
                {
                    row[c] = fields[c];
                }

                rows.Add(row);
            }

            // ORDER BY [StartTime] ASC, as the SQL query does.
            rows.Sort((a, b) => string.CompareOrdinal(
                Convert.ToString(a[0]), Convert.ToString(b[0])));
            foreach (DataRow row in rows)
            {
                table.Rows.Add(row);
            }

            return table;
        }

        /// <summary>Quotes a field so a comma or quote in an alarm description cannot
        /// shift every later column.</summary>
        private static string CsvField(string value)
        {
            if (value == null)
            {
                return "\"\"";
            }

            return "\"" + value.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
        }

        /// <summary>Splits one CSV line, honouring the quoting <see cref="CsvField"/> applies.</summary>
        private static string[] SplitCsvLine(string line)
        {
            var fields = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        // "" inside a quoted field is a literal quote.
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            current.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
                else if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    fields.Add(current.ToString());
                    current.Length = 0;
                }
                else
                {
                    current.Append(c);
                }
            }

            fields.Add(current.ToString());
            return fields.ToArray();
        }

        // The report procedures live in SQL Server; there is no CSV equivalent.
        public DataTable GetMachineStatusReport(string shift, DateTime startTime, DateTime endTime)
        {
            throw new NotImplementedException();
        }

        public DataTable GetMachineHealthReport(string shift, DateTime startTime, DateTime endTime)
        {
            throw new NotImplementedException();
        }

        public int GetPassCountByHour(DateTime startTime, DateTime endTime)
        {
            throw new NotImplementedException();
        }
        //fail
        public int GetFailCountByHour(DateTime startTime, DateTime endTime)
        {
            throw new NotImplementedException();
        }

        public DataTable GetProdData(string _shift, DateTime startTime, DateTime endTime, string barCode)
        {
            throw new NotImplementedException();
        }

        public DataTable GetMachineHealth(string _shift, DateTime startTime, DateTime endTime, string category)
        {
            throw new NotImplementedException();
        }

        public DataTable GetMachineStatus(string _shift, DateTime startTime, DateTime endTime)
        {
            throw new NotImplementedException();
        }

        public DataTable GetMachineProdData(string _shift, DateTime startTime, DateTime endTime, string ProductCode)
        {
            throw new NotImplementedException();
        }
    }
}
