using System;
using Nova.Abstractions.Machine.Models;

namespace Nova.Abstractions.Services
{
    /// <summary>
    /// Publishes live machine health / status snapshots for UI binding.
    /// Implementations own the polling cadence (machine engine, scheduler, or
    /// simulation); consumers only subscribe. <see cref="TelemetryUpdated"/> may be
    /// raised on a background thread — ViewModels must only set scalar
    /// INotifyPropertyChanged properties from the handler (WPF marshals those),
    /// never mutate bound collections.
    /// </summary>
    public interface IMachineTelemetryService
    {
        /// <summary>Most recent snapshot, or null if none has been produced yet.</summary>
        MachineTelemetrySnapshot Latest { get; }

        event EventHandler<MachineTelemetrySnapshot> TelemetryUpdated;

        void Start();
        void Stop();
    }
}
