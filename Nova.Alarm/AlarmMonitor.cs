using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Nova.Abstractions.Services;
using Teal.Library.Contracts.Models;
using Teal.Library.Log;

namespace Nova.Alarm
{
    /// <summary>
    /// Watches the PLC's alarm list and keeps the HMI's alarms up to date.
    /// </summary>
    /// <remarks>
    /// Every half second:
    /// <list type="number">
    /// <item>read the alarm list from the PLC (<see cref="PlcAlarmReader"/>)</item>
    /// <item>compare it with the alarms we already know (<see cref="AlarmTracker"/>)</item>
    /// <item>save started and ended alarms to the database (<see cref="AlarmDatabase"/>)</item>
    /// <item>tell the screens</item>
    /// </list>
    /// Registered as <see cref="IActiveAlarmService"/>, so the alarm bell follows it.
    ///
    /// Start and end times are the moment the HMI sees the change, so they are accurate
    /// to about half a second. When the PLC stamps its own raise time into each alarm,
    /// use that instead.
    /// </remarks>
    public sealed class AlarmMonitor : IActiveAlarmService, IDisposable
    {
        private static readonly TimeSpan CheckInterval = TimeSpan.FromMilliseconds(500);

        private readonly PlcAlarmReader _plc;
        private readonly AlarmDatabase _database;   // null = alarms are shown but not saved
        private readonly AlarmTracker _tracker = new AlarmTracker();
        private readonly object _sync = new object();

        private Timer _timer;
        private int _checkRunning;                  // 1 while a check is running
        private bool _openAlarmsLoaded;
        private bool _plcReadFailing;
        private SynchronizationContext _uiThread;
        private IReadOnlyList<ActiveAlarmSnapshot> _activeAlarms = new ActiveAlarmSnapshot[0];

        /// <summary>Raised on the UI thread when the list of active alarms changes.</summary>
        public event EventHandler<IReadOnlyList<ActiveAlarmSnapshot>> ActiveAlarmsChanged;

        /// <summary>Raised on the monitor thread when an alarm starts (for HIVE reporting).</summary>
        public event EventHandler<AlarmRecord> AlarmStarted;

        /// <summary>Raised on the monitor thread when an alarm ends (for HIVE reporting).</summary>
        public event EventHandler<AlarmRecord> AlarmEnded;

        /// <param name="plc">Reads the alarm list from the PLC.</param>
        /// <param name="database">Saves the alarms. Null to only show them.</param>
        public AlarmMonitor(PlcAlarmReader plc, AlarmDatabase database)
        {
            _plc = plc;
            _database = database;
        }

        public IReadOnlyList<ActiveAlarmSnapshot> GetActiveAlarms()
        {
            lock (_sync)
            {
                return _activeAlarms;
            }
        }

        public void Start()
        {
            lock (_sync)
            {
                if (_timer != null)
                    return;

                // Start() is called on the UI thread; screen updates are sent back to it.
                _uiThread = SynchronizationContext.Current;
                _timer = new Timer(OnTimer, null, TimeSpan.Zero, CheckInterval);
            }
        }

        public void Stop()
        {
            lock (_sync)
            {
                if (_timer != null)
                {
                    _timer.Dispose();
                    _timer = null;
                }
            }
        }

        public void Dispose()
        {
            Stop();
            _plc.Dispose();
        }

        private void OnTimer(object state)
        {
            // Skip this tick if the previous check is still running.
            if (Interlocked.Exchange(ref _checkRunning, 1) == 1)
                return;

            try
            {
                CheckAlarms();
            }
            catch (Exception ex)
            {
                LogError("Alarm check failed", ex);
            }
            finally
            {
                Interlocked.Exchange(ref _checkRunning, 0);
            }
        }

        private void CheckAlarms()
        {
            // Once, on the first check: alarms still active from the last time the HMI ran.
            if (!_openAlarmsLoaded)
            {
                _openAlarmsLoaded = true;
                LoadOpenAlarms();
            }

            List<PlcAlarm> plcAlarms = ReadPlcAlarms();
            if (plcAlarms == null)
                return;   // PLC not readable: leave every alarm as it is until we can see the PLC again

            AlarmChanges changes = _tracker.Compare(plcAlarms, DateTime.Now);
            if (!changes.HasChanges)
                return;

            foreach (AlarmRecord alarm in changes.Started)
            {
                SaveStart(alarm);
                AlarmStarted?.Invoke(this, alarm);
            }

            foreach (AlarmRecord alarm in changes.Ended)
            {
                SaveEnd(alarm);
                AlarmEnded?.Invoke(this, alarm);
            }

            PublishActiveAlarms();
        }

        private void LoadOpenAlarms()
        {
            if (_database == null)
                return;

            try
            {
                _tracker.AddOpenAlarms(_database.LoadOpenAlarms());
                PublishActiveAlarms();
            }
            catch (Exception ex)
            {
                LogError("Could not load the open alarms from the database", ex);
            }
        }

        /// <summary>
        /// Reads the PLC, or returns null if it cannot be read. The error is logged once,
        /// not every half second.
        /// </summary>
        private List<PlcAlarm> ReadPlcAlarms()
        {
            try
            {
                List<PlcAlarm> alarms = _plc.ReadAlarms();
                _plcReadFailing = false;
                return alarms;
            }
            catch (Exception ex)
            {
                if (!_plcReadFailing)
                {
                    _plcReadFailing = true;
                    LogError("Cannot read the alarm list " + PlcAlarmReader.AlarmListSymbol + " from the PLC", ex);
                }

                return null;
            }
        }

        private void SaveStart(AlarmRecord alarm)
        {
            if (_database == null)
                return;

            try
            {
                _database.SaveStart(alarm);
            }
            catch (Exception ex)
            {
                LogError("Could not save the start of alarm " + alarm.ErrorCode, ex);
            }
        }

        private void SaveEnd(AlarmRecord alarm)
        {
            if (_database == null)
                return;

            try
            {
                _database.SaveEnd(alarm);
            }
            catch (Exception ex)
            {
                LogError("Could not save the end of alarm " + alarm.ErrorCode, ex);
            }
        }

        /// <summary>Updates the active-alarm list and tells the screens, newest alarm first.</summary>
        private void PublishActiveAlarms()
        {
            List<ActiveAlarmSnapshot> snapshots = _tracker.ActiveAlarms
                .OrderByDescending(alarm => alarm.StartTime)
                .Select(alarm => new ActiveAlarmSnapshot(
                    alarm.StartTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    alarm.ErrorCode + " - " + alarm.ErrorDetail))
                .ToList();

            lock (_sync)
            {
                _activeAlarms = snapshots;
            }

            if (_uiThread != null)
                _uiThread.Post(_ => RaiseActiveAlarmsChanged(snapshots), null);
            else
                RaiseActiveAlarmsChanged(snapshots);
        }

        private void RaiseActiveAlarmsChanged(IReadOnlyList<ActiveAlarmSnapshot> snapshots)
        {
            try
            {
                ActiveAlarmsChanged?.Invoke(this, snapshots);
            }
            catch (Exception ex)
            {
                // A faulty screen must not stop alarm updates.
                LogError("An ActiveAlarmsChanged handler failed", ex);
            }
        }

        private static void LogError(string message, Exception ex)
        {
            Logger.WriteLog(LogType.Error, "Alarm", "PLC", message + ": " + ex.Message, ex.ToString());
        }
    }
}
