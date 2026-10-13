using System;

namespace Master.Repository
{
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
}
