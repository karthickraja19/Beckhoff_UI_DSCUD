using System;

namespace Nova.Alarm
{
    /// <summary>
    /// One alarm the HMI is tracking: what it is, when it started and when it ended.
    /// This is also one row of the [Alarms] table.
    /// </summary>
    public sealed class AlarmRecord
    {
        public string ErrorCode { get; set; }

        public string ErrorType { get; set; }

        public string ErrorDetail { get; set; }

        public DateTime StartTime { get; set; }

        /// <summary>Empty while the alarm is still active.</summary>
        public DateTime? EndTime { get; set; }

        /// <summary>ID of the alarm's row in [Alarms]; 0 when it has not been saved yet.</summary>
        public int DatabaseId { get; set; }

        /// <summary>True when the PLC entry is this same alarm.</summary>
        public bool Matches(PlcAlarm plcAlarm)
        {
            return ErrorCode == plcAlarm.ErrorCode
                && ErrorType == plcAlarm.ErrorType
                && ErrorDetail == plcAlarm.ErrorDetail;
        }
    }
}
