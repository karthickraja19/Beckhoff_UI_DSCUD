using System;
using Nova.Abstractions.Hardware;

namespace Nova.Abstractions.Services
{
    /// <summary>
    /// Publishes live axis status snapshots for UI binding — one event per axis per
    /// poll cycle. The WPF replacement for the WinForms per-form polling timers.
    /// Raised on a background thread: handlers must only set scalar
    /// INotifyPropertyChanged properties, never mutate bound collections.
    /// </summary>
    public interface IAxisMonitorService
    {
        event EventHandler<AxisStatusSnapshot> AxisStatusUpdated;

        void Start();
        void Stop();
    }
}
