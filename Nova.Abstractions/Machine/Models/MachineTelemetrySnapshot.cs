using System;

namespace Nova.Abstractions.Machine.Models
{
    /// <summary>
    /// Point-in-time read-back of machine health (time buckets) and shift/production
    /// status. Platform-free replacement for the V3 GlobalClass.Health +
    /// GlobalClass.ShiftStatusData pair that the WinForms HomeScreen timer polled.
    /// </summary>
    public class MachineTelemetrySnapshot
    {
        public string ShiftName { get; set; }
        public string MachineMode { get; set; }

        // ---- Machine health time buckets (seconds) ----
        public double RunTimeSeconds { get; set; }
        public double DownTimeSeconds { get; set; }
        public double IdleTimeSeconds { get; set; }
        public double TotalTimeSeconds => RunTimeSeconds + DownTimeSeconds + IdleTimeSeconds;

        // ---- Production counters ----
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public int OverallCount => PassCount + FailCount;

        // ---- Rates / KPIs ----
        public double CycleTimeSeconds { get; set; }
        public double RetestRatePercent { get; set; }
        public double YieldPercent { get; set; }
        public double OeePercent { get; set; }
        public double TossingRatePercent { get; set; }
        public double MtbfHours { get; set; }
        public double MtbaHours { get; set; }
        public int Uph { get; set; }

        public DateTime LastUpdated { get; set; }
    }
}
