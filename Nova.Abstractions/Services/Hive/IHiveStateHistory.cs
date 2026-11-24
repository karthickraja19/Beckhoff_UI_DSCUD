using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nova.Abstractions.Services
{
    /// <summary>One HIVE state change: the state the machine went into, and when.</summary>
    public sealed class HiveStateChangeRecord
    {
        public DateTime Time { get; set; }

        /// <summary>1 running, 2 idle, 5 downtime.</summary>
        public int State { get; set; }

        /// <summary>The error code sent with a state 5 change, e.g. "PD-02". Null otherwise.</summary>
        public string Code { get; set; }
    }

    /// <summary>
    /// The state changes the machine has made, for the Home page's State Changes chart
    /// (Audio SW DFM v5.4 slide 43: "shows past 7 days' state data"). Implemented over
    /// [HealthData] (Master.Repository's HealthStateHistory); state 0 is a time with no record.
    /// </summary>
    public interface IHiveStateHistory
    {
        /// <summary>
        /// Every change from <paramref name="start"/> to <paramref name="end"/>, oldest
        /// first, plus the last change before <paramref name="start"/> when there is one,
        /// so the caller knows what state the machine was in at the start.
        /// </summary>
        Task<IReadOnlyList<HiveStateChangeRecord>> GetStateChangesAsync(DateTime start, DateTime end);
    }
}
