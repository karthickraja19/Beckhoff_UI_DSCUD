using System;
using System.Collections.Generic;

namespace Nova.Abstractions.Services
{
    /// <summary>One maintenance item of one fixture (S11.3 Maintenance Tracking V1.2).</summary>
    public sealed class MaintenanceItem
    {
        /// <summary>CM project code, e.g. "H38".</summary>
        public string Project { get; set; } = string.Empty;

        /// <summary>Line code, e.g. "R1".</summary>
        public string Line { get; set; } = string.Empty;

        /// <summary>The fixture ("Head ID"), e.g. "Front Gantry".</summary>
        public string HeadId { get; set; } = string.Empty;

        /// <summary>The maintenance item's name, e.g. "Needle cleaning".</summary>
        public string Item { get; set; } = string.Empty;

        /// <summary>Maint. Period, hours: a positive whole number.</summary>
        public int PeriodHours { get; set; }

        /// <summary>Alarm-triggering Threshold, hours: a positive whole number.</summary>
        public int ThresholdHours { get; set; }

        /// <summary>Latest maintenance record, to the hour; null until MES accepted the first one.</summary>
        public DateTime? LatestRecord { get; set; }

        /// <summary>What MES knows the item as: fixture and item joined, e.g. "701_Pressure paper".</summary>
        public string ItemCode
        {
            get { return (HeadId ?? string.Empty).Trim() + "_" + (Item ?? string.Empty).Trim(); }
        }

        /// <summary>Scheduled Maint. Time = latest record + period.</summary>
        public DateTime? Scheduled
        {
            get { return LatestRecord.HasValue ? LatestRecord.Value.AddHours(PeriodHours) : (DateTime?)null; }
        }

        public MaintenanceItem Clone()
        {
            return (MaintenanceItem)MemberwiseClone();
        }
    }

    /// <summary>
    /// The maintenance items of this machine's fixtures, kept in the machine's configuration
    /// (S11.3: "update the item's information to the configuration file" after MES said true).
    /// </summary>
    public interface IMaintenanceStore
    {
        /// <summary>The fixtures of this machine, e.g. "Front Gantry", "Rear Gantry", in the order shown.</summary>
        IReadOnlyList<string> Fixtures { get; }

        /// <summary>Every item, of every fixture.</summary>
        IReadOnlyList<MaintenanceItem> Items { get; }

        /// <summary>Adds the item, or replaces the one with the same fixture and item name.</summary>
        bool Save(MaintenanceItem item, out string error);

        /// <summary>Removes the item with this fixture and item name.</summary>
        bool Remove(string headId, string item, out string error);

        /// <summary>Raised after a save or a removal. May be raised on any thread.</summary>
        event EventHandler Changed;
    }
}
