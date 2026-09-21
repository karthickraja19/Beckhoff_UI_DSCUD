using System;

namespace Master.Repository
{
    public class Shift
    {
        public string ShiftName { get; set; }

        public TimeSpan StartTime { get; set; }

        public TimeSpan EndTime { get; set; }
    }
}
