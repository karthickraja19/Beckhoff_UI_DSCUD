using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nova.Abstractions.Machine.Enums;

namespace Nova.Abstractions.Services
{
    /// <summary>
    /// UI-facing view of the machine alarm engine. The engine itself is the V3
    /// Master.AlarmHandler.AlarmHelper; this contract exists only so the WPF views
    /// can bind to it without referencing WinForms or the legacy statics.
    /// </summary>
    public interface IAlarmService
    {
        bool HasActiveAlarm { get; }

        /// <summary>
        /// Alarms raised during this application session, oldest first. Session-scoped
        /// by design: durable history lives in the [Alarms] table and is read back
        /// through <see cref="Nova.Abstractions.Data.Repositories.IAlarmLogRepository"/>.
        /// </summary>
        IReadOnlyList<AlarmEventArgs> GetHistory();

        Task TriggerAlarmAsync(ECode eCode, bool isMotionError, int type, string detailName = "");
        void TriggerAlarm(ECode eCode, bool isMotionError, int type, string detailName = "");
        void TriggerAlarm(string deviceId, string alarmType, string description);
        void ResetAllAlarms();

        event EventHandler<AlarmEventArgs> OnAlarmTriggered;
        event EventHandler<AlarmEventArgs> OnAlarmStateChanged;
    }
}
