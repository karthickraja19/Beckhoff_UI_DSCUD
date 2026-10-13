using System;

namespace Master.Repository
{
    /// <summary>One row of [Alarms] that has no EndTime yet: an alarm still active.</summary>
    public class OpenAlarm
    {
        public int Id { get; set; }
        public string AlarmCode { get; set; }
        public string AlarmType { get; set; }
        public string AlarmDescription { get; set; }
        public DateTime StartTime { get; set; }
    }
}
