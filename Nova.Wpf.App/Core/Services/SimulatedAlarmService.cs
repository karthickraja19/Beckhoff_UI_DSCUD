using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nova.Abstractions.Machine.Enums;
using Nova.Abstractions.Services;

namespace Nova.Core.Services
{
    /// <summary>
    /// In-memory <see cref="IAlarmService"/> for running without a PLC. On the machine,
    /// alarms are raised by TwinCAT EventLogger and reach the HMI through an adapter that
    /// replaces this one. Kept so the alarm screens and the simulated orchestrator have
    /// something to drive.
    /// </summary>
    public sealed class SimulatedAlarmService : IAlarmService
    {
        private readonly object _sync = new object();
        private readonly List<AlarmEventArgs> _history = new List<AlarmEventArgs>();
        private readonly Dictionary<string, AlarmEventArgs> _active = new Dictionary<string, AlarmEventArgs>();

        public event EventHandler<AlarmEventArgs> OnAlarmTriggered;
        public event EventHandler<AlarmEventArgs> OnAlarmStateChanged;

        public bool HasActiveAlarm
        {
            get { lock (_sync) { return _active.Count > 0; } }
        }

        public IReadOnlyList<AlarmEventArgs> GetHistory()
        {
            lock (_sync) { return _history.ToList(); }
        }

        public Task TriggerAlarmAsync(ECode eCode, bool isMotionError, int type, string detailName = "")
        {
            TriggerAlarm(eCode, isMotionError, type, detailName);
            return Task.CompletedTask;
        }

        public void TriggerAlarm(ECode eCode, bool isMotionError, int type, string detailName = "")
        {
            string code = eCode.ToString();
            Raise(code, new AlarmEventArgs
            {
                ErrorCode = eCode,
                ErrorCodeString = code,
                ErrorName = code,
                ErrorContent = detailName ?? string.Empty,
                ErrorDescription = string.IsNullOrEmpty(detailName) ? code : code + " - " + detailName,
            });
        }

        public void TriggerAlarm(string deviceId, string alarmType, string description)
        {
            Raise(deviceId + "|" + alarmType, new AlarmEventArgs
            {
                ErrorCode = ECode.ProgramException_Error,
                ErrorCodeString = alarmType,
                ErrorName = deviceId,
                ErrorContent = description,
                ErrorDescription = string.IsNullOrEmpty(deviceId) ? description : deviceId + " - " + description,
            });
        }

        public void ResetAllAlarms()
        {
            lock (_sync)
            {
                foreach (var alarm in _active.Values)
                    alarm.IsActive = false;
                _active.Clear();
            }

            OnAlarmStateChanged?.Invoke(this, new AlarmEventArgs
            {
                ErrorCode = ECode.Invalid_ECode,
                IsActive = false,
                OccurredTime = DateTime.Now
            });
        }

        private void Raise(string key, AlarmEventArgs args)
        {
            lock (_sync)
            {
                // A standing alarm is not raised again, same as the V3 engine.
                if (_active.ContainsKey(key))
                    return;

                args.OccurredTime = DateTime.Now;
                args.IsActive = true;
                _active[key] = args;
                _history.Add(args);
            }

            OnAlarmTriggered?.Invoke(this, args);
            OnAlarmStateChanged?.Invoke(this, args);
        }
    }
}
