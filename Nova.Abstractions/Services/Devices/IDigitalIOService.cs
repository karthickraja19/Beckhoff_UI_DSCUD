using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nova.Abstractions.Hardware;

namespace Nova.Abstractions.Services
{
    /// <summary>
    /// Live digital IO for the whole machine. Points are addressed by the logical
    /// names from Data\Config.csv (config-driven — no per-machine enums), with bit
    /// overloads for hardware-level callers. Implementations own the polling
    /// cadence; <see cref="IOChanged"/> may be raised on a background thread, so
    /// handlers must only set scalar INotifyPropertyChanged properties.
    /// </summary>
    public interface IDigitalIOService
    {
        /// <summary>Current snapshot of every input, ordered by bit.</summary>
        IReadOnlyList<IOPointSnapshot> ReadInputs();

        /// <summary>Current snapshot of every output, ordered by bit.</summary>
        IReadOnlyList<IOPointSnapshot> ReadOutputs();

        bool ReadInput(string name);
        bool ReadOutput(string name);

        Task<bool> WriteOutputAsync(string name, bool on);
        Task<bool> WriteOutputAsync(int bit, bool on);

        /// <summary>
        /// Latest raw input array indexed by bit — for legacy-style hardware
        /// callbacks (IO steppers, cylinders) that consume the whole bank.
        /// </summary>
        bool[] GetAllInputs();

        /// <summary>Raised after each poll cycle or output write (background thread).</summary>
        event EventHandler IOChanged;

        void Start();
        void Stop();
    }
}
