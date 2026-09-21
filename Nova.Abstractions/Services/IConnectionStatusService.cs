using System;
using System.Collections.Generic;

namespace Nova.Abstractions.Services
{
    /// <summary>
    /// One device row on the Home screen's Connection Status panel — the same three
    /// columns V3's HomeScreen.RefreshConnectionStatus() builds its DataTable from
    /// (Type, Device, Status).
    /// </summary>
    public sealed class ConnectionStatusEntry
    {
        /// <summary>Group label: "CCD", "Scanner", "Robot", "COM", "Motion Card".</summary>
        public string DeviceType { get; set; }

        public string Name { get; set; }

        public bool IsConnected { get; set; }
    }

    /// <summary>
    /// Live connection state for the machine's external devices. Implementations own
    /// the polling cadence; <see cref="StatusChanged"/> may be raised on a background
    /// thread, so handlers must only set scalar INotifyPropertyChanged properties and
    /// never mutate bound collections.
    /// </summary>
    public interface IConnectionStatusService
    {
        /// <summary>Current snapshot, in the group order the panel renders.</summary>
        IReadOnlyList<ConnectionStatusEntry> Read();

        /// <summary>Raised after each poll cycle (background thread).</summary>
        event EventHandler StatusChanged;

        void Start();
        void Stop();
    }
}
