using System;

namespace Nova.Abstractions.Data.Models
{
    public class AlarmData
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string AlarmCode { get; set; }
        public string AlarmDescription { get; set; }
        public string AlarmType { get; set; }
        public string Shift { get; set; }
    }

    public class HealthData
    {
        public DateTime Date { get; set; }
        public string Shift { get; set; }
        public string HealthCategory { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public double TotalTime { get; set; }
    }

    public class OEEData
    {
        public DateTime DateTime { get; set; }
        public double Availability { get; set; }
        public double Performance { get; set; }
        public double Quality { get; set; }
    }

    public class ProductionData
    {
        public DateTime Date { get; set; }
        public string Shift { get; set; }
        public DateTime ProductionTime { get; set; }
        public string ProductCode { get; set; }
        public bool ProductionStatus { get; set; }
        public double CycleTime { get; set; }
        public int Iteration { get; set; }

        /// <summary>True when the unit was tossed by the pre-process CCD check.</summary>
        public bool Tossing { get; set; }
    }

    public class ProductionDataHourly
    {
        public DateTime Date { get; set; }
        public string Shift { get; set; }
        public int Hour { get; set; }
        public int ProductCount { get; set; }
    }

    public class Shift
    {
        public string ShiftName { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
    }

    public class ShiftData
    {
        public DateTime Date { get; set; }
        public string Shift { get; set; }
        public double RunTime { get; set; }
        public double DownTime { get; set; }
        public double IdleTime { get; set; }
        public double PlannedDownTime { get; set; } = 0;
        public double BreakTime { get; set; } = 0;
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public double Yield { get; set; }
        public double OEE { get; set; }
        public double MTBF { get; set; }
        public double MTBA { get; set; }
        public double TossingRate { get; set; }
        public double RetestRate { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}
