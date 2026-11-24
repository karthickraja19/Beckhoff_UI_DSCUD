using System;
using System.Collections.Generic;
using Nova.Abstractions.Hardware;

namespace Nova.Abstractions.Services
{
    /// <summary>
    /// Publishes live loadcell snapshots for UI binding. Raised on a background
    /// thread: handlers must only set scalar INPC properties.
    /// </summary>
    public interface ILoadCellMonitorService
    {
        event EventHandler<LoadCellStatusSnapshot> LoadCellStatusUpdated;

        IReadOnlyList<LoadCellStatusSnapshot> ReadAll();

        void Start();
        void Stop();
    }
}
