using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nova.Abstractions.Services;
using Teal.Library.Contracts.Models;
using Teal.Library.Log;

namespace Nova.Alarm
{
    /// <summary>
    /// Listens to the PLC's alarm list and keeps the HMI's alarms up to date.
    /// </summary>
    /// <remarks>
    /// Every time the PLC sends its alarm list (<see cref="PlcAlarmReader"/> subscribes to it):
    /// <list type="number">
    /// <item>compare it with the alarms we already know (<see cref="AlarmTracker"/>)</item>
    /// <item>save started and ended alarms to the database (<see cref="AlarmDatabase"/>)</item>
    /// <item>tell the screens</item>
    /// </list>
    /// Registered as <see cref="IActiveAlarmService"/>, so the alarm bell follows it.
    ///
    /// The lists are handled one at a time, in the order they arrived, on a worker thread
    /// rather than the ADS thread, so a slow database cannot hold up the PLC connection.
    /// Start and end times are the PLC's time stamp on the list, so they are accurate to
    /// the 100 ms the PLC checks the list at.
    /// </remarks>
    public sealed class AlarmMonitor : IActiveAlarmService, IDisposable
    {
        private readonly PlcAlarmReader _plc;
        private readonly AlarmDatabase _database;   // null = alarms are shown but not saved
        private readonly AlarmTracker _tracker = new AlarmTracker();
        private readonly object _sync = new object();

        private bool _started;
        private bool _openAlarmsLoaded;
        private Task _work = Task.CompletedTask;    // the work waiting to be done, in order
        private SynchronizationContext _uiThread;
        private IReadOnlyList<ActiveAlarmSnapshot> _activeAlarms = new ActiveAlarmSnapshot[0];

        /// <summary>Raised on the UI thread when the list of active alarms changes.</summary>
        public event EventHandler<IReadOnlyList<ActiveAlarmSnapshot>> ActiveAlarmsChanged;

        /// <summary>Raised on a worker thread when an alarm starts (for HIVE reporting).</summary>
        public event EventHandler<AlarmRecord> AlarmStarted;

        /// <summary>Raised on a worker thread when an alarm ends (for HIVE reporting).</summary>
        public event EventHandler<AlarmRecord> AlarmEnded;

        /// <param name="plc">Subscribes to the alarm list in the PLC.</param>
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
            bool loadOpenAlarms;

            lock (_sync)
            {
                if (_started)
                    return;

                _started = true;

                // Start() is called on the UI thread; screen updates are sent back to it.
                _uiThread = SynchronizationContext.Current;

                loadOpenAlarms = !_openAlarmsLoaded;
                _openAlarmsLoaded = true;
            }

            // Once, and first: alarms still active from the last time the HMI ran, so the
            // first list from the PLC is compared with them.
            if (loadOpenAlarms)
                Queue(LoadOpenAlarms);

            _plc.AlarmsChanged += OnPlcAlarmsChanged;
            _plc.Start();
        }

        public void Stop()
        {
            lock (_sync)
            {
                if (!_started)
                    return;

                _started = false;
            }

            _plc.AlarmsChanged -= OnPlcAlarmsChanged;
            _plc.Stop();
        }

        public void Dispose()
        {
            Stop();
            _plc.Dispose();
        }

        /// <summary>
        /// The PLC sent its alarm list. This is the ADS thread, so the work is only queued.
        /// If the PLC cannot be reached no lists arrive, and every alarm stays as it is
        /// until the PLC is back.
        /// </summary>
        private void OnPlcAlarmsChanged(object sender, PlcAlarmList list)
        {
            Queue(() => CheckAlarms(list));
        }

        /// <summary>Runs the work after everything queued before it, on a worker thread.</summary>
        private void Queue(Action work)
        {
            lock (_sync)
            {
                _work = _work.ContinueWith(_ => Run(work), TaskScheduler.Default);
            }
        }

        private static void Run(Action work)
        {
            try
            {
                work();
            }
            catch (Exception ex)
            {
                LogError("Alarm check failed", ex);
            }
        }

        private void CheckAlarms(PlcAlarmList list)
        {
            AlarmChanges changes = _tracker.Compare(list.Alarms, list.Time);
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
