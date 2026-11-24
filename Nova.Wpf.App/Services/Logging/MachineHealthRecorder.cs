using System;
using System.Threading.Tasks;
using Master.SharedUtilities;
using Nova.Alarm;
using Nova.Hive;
using Teal.Library.Log;

namespace Nova.Wpf.App.Services
{
    /// <summary>
    /// Feeds [HealthData] - the run, down and idle time the shift status and OEE are worked
    /// out from - from the PLC's E_MachineState, pushed through the shared subscriptions.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>AUTO_RUNNING: RunTime.</item>
    /// <item>ALARM: DownTime.</item>
    /// <item>IDLE, PAUSE, HOMING, MANUAL, STOP: IdleTime.</item>
    /// <item>PLC not live, an unknown value, or the HMI closed: nothing is counted.</item>
    /// </list>
    /// Each change closes the segment before it, written as one row by
    /// <see cref="ShiftStatusHelper.SetHealthCategory"/>; the segment under way is counted into
    /// the totals as it goes, and split at a shift change by Master.Scheduler. Started after
    /// Master.Scheduler, which gives the helper its data access and shift; does nothing
    /// without a database or a shift.
    /// </remarks>
    public sealed class MachineHealthRecorder : IDisposable
    {
        private readonly PlcSubscriptions _subscriptions;
        private readonly string _symbol;
        private readonly object _sync = new object();

        /// <summary>Changes are written in order, off the ADS thread: they go to the database.</summary>
        private Task _delivery = Task.FromResult(0);

        private bool _started;
        private bool _hasCategory;
        private string _category;

        /// <param name="symbol">E_MachineState (symbol map: Status.MachineState); blank records nothing.</param>
        public MachineHealthRecorder(PlcSubscriptions subscriptions, string symbol)
        {
            _subscriptions = subscriptions ?? throw new ArgumentNullException(nameof(subscriptions));
            _symbol = symbol ?? string.Empty;
        }

        public void Start()
        {
            if (_symbol.Length == 0)
            {
                Logger.WriteLog(LogType.Info, "Scheduler", "MachineHealth",
                    "The symbol map has no Status.MachineState, so no run, down or idle time is recorded.", string.Empty);
                return;
            }

            lock (_sync)
            {
                if (_started)
                    return;

                _started = true;
            }

            _subscriptions.ValueChanged += OnValueChanged;
            _subscriptions.LiveChanged += OnLiveChanged;

            // E_MachineState is an INT in the PLC, so two bytes.
            _subscriptions.Watch(_symbol, typeof(short));
            Report();
        }

        /// <summary>Closes the segment under way, so the time up to now is kept, and waits for it to be written.</summary>
        public void Stop()
        {
            lock (_sync)
            {
                if (!_started)
                    return;

                _started = false;
                _subscriptions.ValueChanged -= OnValueChanged;
                _subscriptions.LiveChanged -= OnLiveChanged;
                Enqueue(null, DateTime.Now);
            }

            try
            {
                _delivery.Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception)
            {
                // Logged where it failed.
            }
        }

        public void Dispose()
        {
            Stop();
        }

        /// <summary>Runs on the ADS thread: no ADS calls, and nothing slow.</summary>
        private void OnValueChanged(object sender, string symbol)
        {
            if (string.Equals(symbol, _symbol, StringComparison.OrdinalIgnoreCase))
                Report();
        }

        private void OnLiveChanged(object sender, EventArgs e)
        {
            Report();
        }

        private void Report()
        {
            short value;
            string category = _subscriptions.TryGet(_symbol, out value)
                ? CategoryOf((PlcMachineState)value)
                : null;

            lock (_sync)
            {
                if (!_started)
                    return;

                Enqueue(category, DateTime.Now);
            }
        }

        /// <summary>Under <see cref="_sync"/>.</summary>
        private void Enqueue(string category, DateTime at)
        {
            if (_hasCategory && category == _category)
                return;

            _hasCategory = true;
            _category = category;
            _delivery = _delivery.ContinueWith(_ => Write(category, at), TaskScheduler.Default);
        }

        private static void Write(string category, DateTime at)
        {
            try
            {
                ShiftStatusHelper.SetHealthCategory(category, at);
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "Scheduler", "MachineHealth",
                    "The " + (category ?? "unknown") + " time from " + at.ToString("HH:mm:ss")
                    + " could not be recorded: " + ex.Message, ex.ToString());
            }
        }

        private static string CategoryOf(PlcMachineState state)
        {
            switch (state)
            {
                case PlcMachineState.AutoRunning:
                    return ShiftStatusHelper.RunTime;
                case PlcMachineState.Alarm:
                    return ShiftStatusHelper.DownTime;
                case PlcMachineState.Idle:
                case PlcMachineState.Pause:
                case PlcMachineState.Homing:
                case PlcMachineState.Manual:
                case PlcMachineState.Stop:
                    return ShiftStatusHelper.IdleTime;
                default:
                    return null;
            }
        }
    }
}
