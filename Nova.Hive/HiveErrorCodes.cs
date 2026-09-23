using System;
using System.Collections.Generic;
using System.IO;
using Nova.Alarm;
using Teal.Library.Contracts.Models;
using Teal.Library.Log;

namespace Nova.Hive
{
    /// <summary>One alarm written the way HIVE wants it reported.</summary>
    public sealed class HiveError
    {
        /// <summary>HIVE error code, e.g. "M03CVSO-01".</summary>
        public string Code { get; set; }

        /// <summary>Error type from the DFM list, e.g. "Motion Error".</summary>
        public string Message { get; set; }

        /// <summary>What actually happened, in plain words.</summary>
        public string Detail { get; set; }

        /// <summary>"warning", "error" or "critical".</summary>
        public string Severity { get; set; }

        /// <summary>True when this alarm stops the machine, which puts HIVE into downtime.</summary>
        public bool StopsMachine { get; set; }

        public HiveError Copy()
        {
            return (HiveError)MemberwiseClone();
        }
    }

    /// <summary>
    /// Maps a PLC alarm code to the HIVE error code, message and detail.
    /// </summary>
    /// <remarks>
    /// The mapping is a CSV file (Data\HiveErrorCodes.csv) so codes can be corrected
    /// without rebuilding the software. A code that is not in the file is reported as
    /// "Others" with the PLC's own text, so an alarm is never lost - the missing line
    /// is written to the log instead.
    /// </remarks>
    public sealed class HiveErrorCodes
    {
        private readonly Dictionary<string, HiveError> _byPlcCode =
            new Dictionary<string, HiveError>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Reads the mapping file. A missing or broken line is skipped and logged.</summary>
        public static HiveErrorCodes Load(string csvFile)
        {
            var codes = new HiveErrorCodes();

            if (string.IsNullOrWhiteSpace(csvFile) || !File.Exists(csvFile))
            {
                LogError("HIVE error code file not found: " + csvFile + ". Every alarm will be reported as Others.");
                return codes;
            }

            foreach (string line in File.ReadAllLines(csvFile))
            {
                string text = line.Trim();

                // Skip blank lines, comments and the header row.
                if (text.Length == 0 || text.StartsWith("#") || text.StartsWith("PlcCode", StringComparison.OrdinalIgnoreCase))
                    continue;

                // PlcCode,HiveCode,ErrorMessage,ErrorDetail,Severity,StopsMachine
                string[] columns = text.Split(',');
                if (columns.Length < 6)
                {
                    LogError("Line ignored in " + csvFile + " (needs 6 columns): " + text);
                    continue;
                }

                codes._byPlcCode[columns[0].Trim()] = new HiveError
                {
                    Code = columns[1].Trim(),
                    Message = columns[2].Trim(),
                    Detail = columns[3].Trim(),
                    Severity = columns[4].Trim(),
                    StopsMachine = columns[5].Trim().Equals("yes", StringComparison.OrdinalIgnoreCase),
                };
            }

            return codes;
        }

        /// <summary>The HIVE error for one alarm from the PLC.</summary>
        public HiveError Find(AlarmRecord alarm)
        {
            HiveError known;
            if (_byPlcCode.TryGetValue(alarm.ErrorCode ?? "", out known))
            {
                HiveError error = known.Copy();

                // The file holds the approved wording; fall back to the PLC's own text.
                if (string.IsNullOrWhiteSpace(error.Detail))
                    error.Detail = alarm.ErrorDetail;

                return error;
            }

            LogError("PLC alarm code \"" + alarm.ErrorCode + "\" is not in the HIVE error code file. "
                + "Reported as Others - add the code to Data\\HiveErrorCodes.csv.");

            return Others(alarm.ErrorDetail);
        }

        /// <summary>
        /// The DFM's catch-all error type, for downtime that has no code of its own yet:
        /// an unknown alarm, a machine that stopped without one, or downtime the
        /// operator chose by hand. AME still has to give these a proper code.
        /// </summary>
        public static HiveError Others(string detail)
        {
            return new HiveError
            {
                Code = "O99OOOO-01",
                Message = "Others",
                Detail = detail,
                Severity = "error",
                StopsMachine = true,
            };
        }

        private static void LogError(string message)
        {
            Logger.WriteLog(LogType.Error, "Hive", "ErrorCodes", message, "");
        }
    }
}
