using System;

namespace Master.Repository
{
    public class ProductionDataHourly
    {
        public DateTime Date { get; set; }
        public string Shift { get; set; }
        public int Hour { get; set; }
        public int ProductCount { get; set; }
    }
}
