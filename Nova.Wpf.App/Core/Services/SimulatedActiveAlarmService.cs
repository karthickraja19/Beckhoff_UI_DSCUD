using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Nova.Abstractions.Services;

namespace Nova.Core.Services
{
    /// <summary>
    /// Standing-alarm feed for the Home screen alarm tab, projected from
    /// <see cref="IAlarmService"/>'s active entries whenever an alarm is raised or
    /// cleared. Raised on the dispatcher so subscribers can update bound collections.
    /// </summary>
    public sealed class SimulatedActiveAlarmService : IActiveAlarmService, IDisposable
    {
        private readonly IAlarmService _alarms;
        private readonly object _sync = new object();
        private IReadOnlyList<ActiveAlarmSnapshot> _latest = new ActiveAlarmSnapshot[0];
        private bool _started;

        public event EventHandler<IReadOnlyList<ActiveAlarmSnapshot>> ActiveAlarmsChanged;

        public SimulatedActiveAlarmService(IAlarmService alarms)
        {
            _alarms = alarms ?? throw new ArgumentNullException(nameof(alarms));
        }

        public IReadOnlyList<ActiveAlarmSnapshot> GetActiveAlarms()
        {
            lock (_sync) { return _latest; }
        }

        public void Start()
        {
            lock (_sync)
            {
                if (_started)
                    return;
                _started = true;
            }

            _alarms.OnAlarmStateChanged += OnAlarmStateChanged;
        }

        public void Stop()
        {
            lock (_sync)
            {
                if (!_started)
                    return;
                _started = false;
            }

            _alarms.OnAlarmStateChanged -= OnAlarmStateChanged;
        }

        public void Dispose() => Stop();

        private void OnAlarmStateChanged(object sender, AlarmEventArgs e)
        {
            IReadOnlyList<ActiveAlarmSnapshot> active = _alarms.GetHistory()
                .Where(a => a.IsActive)
                .Select(a => new ActiveAlarmSnapshot(a.OccurredTime.ToString(), a.ErrorDescription))
                .ToList();

            lock (_sync) { _latest = active; }

            var app = Application.Current;
            if (app == null)
                return; // shutting down

            app.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    ActiveAlarmsChanged?.Invoke(this, active);
                }
                catch
                {
                    // A faulty subscriber must not break alarm delivery.
                }
            }));
        }
    }
}
