using System;
using System.Collections.Generic;

namespace Nova.Abstractions.Services
{
    /// <summary>One currently-standing alarm, as the Home screen live panel shows it.</summary>
    public sealed class ActiveAlarmSnapshot
    {
        /// <summary>When the alarm latched (V3's ErrorManage.AlarmStartTime).</summary>
        public string Time { get; }

        /// <summary>"CODE - [detail - ]message", the same string V3's live grid built.</summary>
        public string Description { get; }

        public ActiveAlarmSnapshot(string time, string description)
        {
            Time = time;
            Description = description;
        }
    }

    /// <summary>
    /// Live view of the alarms standing right now — the WPF counterpart of V3's
    /// HomeScreen.UpdateLiveAlarmGrid(), which re-read
    /// AlarmHelper.g_lstAlarmInfo.Where(a =&gt; alarmHappen) once a second.
    ///
    /// This is deliberately not history: entries appear when a fault latches and
    /// disappear when it clears. Durable history is the [Alarms] table,
    /// read back through <see cref="Nova.Abstractions.Data.Repositories.IAlarmLogRepository"/>.
    ///
    /// <see cref="ActiveAlarmsChanged"/> is raised on the UI thread, and only when the
    /// set actually changes, so bound collections can be updated directly without
    /// rebuilding them every tick.
    /// </summary>
    public interface IActiveAlarmService
    {
        event EventHandler<IReadOnlyList<ActiveAlarmSnapshot>> ActiveAlarmsChanged;

        IReadOnlyList<ActiveAlarmSnapshot> GetActiveAlarms();

        void Start();
        void Stop();
    }
}
