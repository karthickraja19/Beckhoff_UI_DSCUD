using System;
using Nova.Abstractions.Hardware;

namespace Nova.Abstractions.Services
{
    /// <summary>
    /// Publishes live axis status snapshots for UI binding — one event each time an axis's
    /// status changes (so possibly none for an axis standing still: take the starting value
    /// from IAxis.ReadStatus).
    /// The WPF replacement for the WinForms per-form polling timers.
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
