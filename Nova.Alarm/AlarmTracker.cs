using System;
using System.Collections.Generic;
using System.Linq;

namespace Nova.Alarm
{
    /// <summary>
    /// Works out when alarms start and end, by comparing the PLC's alarm list with the
    /// list of alarms we are already tracking:
    /// <list type="bullet">
    /// <item>in the PLC list, not in ours - the alarm has just started</item>
    /// <item>in our list, not in the PLC list - the alarm was reset, it has just ended</item>
    /// </list>
    /// No PLC, database or screen code in here, so this logic can be read on its own.
    /// </summary>
    public sealed class AlarmTracker
    {
        private readonly List<AlarmRecord> _activeAlarms = new List<AlarmRecord>();

        /// <summary>The alarms that are active right now.</summary>
        public List<AlarmRecord> ActiveAlarms
        {
            get { return _activeAlarms.ToList(); }
        }

        /// <summary>
        /// Puts back alarms that were still active when the HMI last closed. The next
        /// <see cref="Compare"/> ends the ones the PLC no longer has.
        /// </summary>
        public void AddOpenAlarms(IEnumerable<AlarmRecord> openAlarms)
        {
            _activeAlarms.AddRange(openAlarms);
        }

        /// <summary>
        /// Compares the PLC's current alarm list with ours and returns what changed.
        /// </summary>
        public AlarmChanges Compare(List<PlcAlarm> plcAlarms, DateTime now)
        {
            var changes = new AlarmChanges();

            // 1. Started: in the PLC list but not in ours.
            foreach (PlcAlarm plcAlarm in plcAlarms)
            {
                bool alreadyTracked = _activeAlarms.Any(alarm => alarm.Matches(plcAlarm));
                if (!alreadyTracked)
                {
                    var alarm = new AlarmRecord
                    {
                        ErrorCode = plcAlarm.ErrorCode,
                        ErrorType = plcAlarm.ErrorType,
                        ErrorDetail = plcAlarm.ErrorDetail,
                        StartTime = now,
                    };

                    _activeAlarms.Add(alarm);
                    changes.Started.Add(alarm);
                }
            }

            // 2. Ended: in our list but no longer in the PLC list.
            foreach (AlarmRecord alarm in _activeAlarms.ToList())
            {
                bool stillInPlc = plcAlarms.Any(plcAlarm => alarm.Matches(plcAlarm));
                if (!stillInPlc)
                {
                    alarm.EndTime = now;

                    _activeAlarms.Remove(alarm);
                    changes.Ended.Add(alarm);
                }
            }

            return changes;
        }
    }

    /// <summary>What one <see cref="AlarmTracker.Compare"/> found.</summary>
    public sealed class AlarmChanges
    {
        public List<AlarmRecord> Started { get; } = new List<AlarmRecord>();

        public List<AlarmRecord> Ended { get; } = new List<AlarmRecord>();

        public bool HasChanges
        {
            get { return Started.Count > 0 || Ended.Count > 0; }
        }
    }
}
