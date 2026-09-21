using System;

namespace Master.Repository
{
    public class HealthData
    {
        public DateTime Date { get; set; }
        public string Shift { get; set; }
        public string HealthCategory { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public double TotalTime { get; set; }
    }
}
