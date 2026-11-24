using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Nova.Abstractions.Services;

namespace Nova.ViewModels.Pages.Audio
{
    /// <summary>
    /// Live running log, newest first - shown in the Setting page's Manual tab beside the
    /// manual-operation GUI.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lines come from <see cref="IRunningLogService"/>, which drains
    /// Teal.Library.Log.Logger's queue - the same stream every Logger.WriteLog call
    /// across the configuration, monitor and sequence code writes into. The 20-line cap
    /// and newest-at-top ordering are carried over from the V3 HomeScreen running log so
    /// the panel reads identically to the screen it replaces.
    /// </para>
    /// <para>
    /// The DFM does not specify a running log anywhere; slide 57 leaves the Vendor tab's
    /// content open ("GUI for manual operation"). This panel is therefore an addition
    /// rather than a spec requirement - it is here because an operator driving a
    /// semi-auto station by hand needs to see what the machine just did, and the Machine
    /// Log button on the Main tab only opens a folder of files after the fact.
    /// </para>
    /// </remarks>
    public class RunningLogViewModel : ObservableObject, IDisposable
    {
        /// <summary>V3 kept the visible log at 20 lines; anything older falls off.</summary>
        private const int MaxLines = 20;

        private readonly IRunningLogService _runningLog;
        private bool _disposed;

        public ObservableCollection<string> Logs { get; }

        /// <summary>True while nothing has been logged yet, so the view can show a hint.</summary>
        public bool IsEmpty => Logs.Count == 0;

        public RunningLogViewModel(IRunningLogService runningLog = null)
        {
            Logs = new ObservableCollection<string>();
            _runningLog = runningLog;

            if (_runningLog != null)
                _runningLog.LogsAppended += OnLogsAppended;
        }

        // Raised on the UI thread by the service, so touching Logs directly is safe.
        private void OnLogsAppended(object sender, IReadOnlyList<string> batch)
        {
            if (batch == null)
                return;

            foreach (var line in batch)
            {
                while (Logs.Count >= MaxLines)
                    Logs.RemoveAt(Logs.Count - 1);

                Logs.Insert(0, line);
            }

            OnPropertyChanged(nameof(IsEmpty));
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            if (_runningLog != null)
                _runningLog.LogsAppended -= OnLogsAppended;
        }
    }
}
