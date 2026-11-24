using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Nova.Abstractions.Services;
using Nova.ViewModels.Pages.Audio;

namespace Nova.ViewModels.Pages.Manual
{
    /// <summary>
    /// The manual GUI's Log tab: the Homing and reset buttons on top, then the running log
    /// in one panel and the alarms standing right now in the other, so an operator driving
    /// the station by hand sees what the machine just did and what is stopping it.
    /// </summary>
    /// <remarks>
    /// The alarms are the PLC's live list (<see cref="IActiveAlarmService"/>): an entry
    /// appears when the fault latches and goes when the PLC's RESET clears it. History is
    /// the Alarm page.
    /// </remarks>
    public partial class ManualLogViewModel : ObservableObject, IDisposable
    {
        private readonly IActiveAlarmService _activeAlarms;
        private bool _disposed;

        public RunningLogViewModel RunningLog { get; }

        /// <summary>
        /// The Homing and reset buttons' commands. They belong to the Setting page's Manual
        /// tab, which sets this when it hosts the GUI - homing done pops up from any tab.
        /// Null until then, and the buttons are hidden.
        /// </summary>
        [ObservableProperty]
        private ManualTabViewModel _machineControls;

        public ObservableCollection<ActiveAlarmSnapshot> ActiveAlarms { get; } = new ObservableCollection<ActiveAlarmSnapshot>();

        /// <summary>True while an alarm stands; the view shows a hint when false.</summary>
        public bool HasAlarms => ActiveAlarms.Count > 0;

        public ManualLogViewModel(IRunningLogService runningLog = null, IActiveAlarmService activeAlarms = null)
        {
            RunningLog = new RunningLogViewModel(runningLog);
            _activeAlarms = activeAlarms;

            if (_activeAlarms != null)
            {
                _activeAlarms.ActiveAlarmsChanged += OnActiveAlarmsChanged;
                Apply(_activeAlarms.GetActiveAlarms());
            }
        }

        // Raised on the UI thread by the service, so touching ActiveAlarms directly is safe.
        private void OnActiveAlarmsChanged(object sender, IReadOnlyList<ActiveAlarmSnapshot> alarms)
        {
            Apply(alarms);
        }

        private void Apply(IReadOnlyList<ActiveAlarmSnapshot> alarms)
        {
            ActiveAlarms.Clear();

            if (alarms != null)
            {
                foreach (ActiveAlarmSnapshot alarm in alarms)
                    ActiveAlarms.Add(alarm);
            }

            OnPropertyChanged(nameof(HasAlarms));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            if (_activeAlarms != null)
                _activeAlarms.ActiveAlarmsChanged -= OnActiveAlarmsChanged;

            RunningLog.Dispose();
        }
    }
}
