using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nova.Abstractions.Services
{
    /// <summary>
    /// One kind of third-party device the PLC drives over a serial link (dispensers, the
    /// weighing scale, vibration sensors), for its Manual tab. Each device is a PLC data
    /// block; the tab names what it reads and writes by logical keys ("Start", "WeightG"),
    /// and the PLC variable behind each key is in the HMI's symbol map. A key the map does
    /// not name is simply not offered.
    /// </summary>
    public interface IPlcDeviceService
    {
        /// <summary>The devices of this kind, in the PLC's order (index 0 is the first).</summary>
        IReadOnlyList<string> Devices { get; }

        /// <summary>False when the symbol map names no device of this kind.</summary>
        bool IsAvailable { get; }

        /// <summary>Why <see cref="IsAvailable"/> is false; empty otherwise.</summary>
        string UnavailableReason { get; }

        /// <summary>True while the PLC is reachable and in Run.</summary>
        bool IsLive { get; }

        /// <summary>Why <see cref="IsLive"/> is false; empty while live.</summary>
        string NotLiveReason { get; }

        /// <summary>True when the symbol map names a PLC variable for the key.</summary>
        bool Has(string key);

        /// <summary>Follows every mapped value of the device until the handle is disposed - while its tab is on screen.</summary>
        IDisposable Follow(int device);

        /// <summary>A followed value changed, or the PLC came or went. Raised on any thread.</summary>
        event EventHandler Changed;

        bool TryGetFlag(int device, string key, out bool value);

        /// <summary>The value in the HMI's units (the PLC's raw value divided by the key's scale).</summary>
        bool TryGetNumber(int device, string key, out double value);

        bool TryGetText(int device, string key, out string value);

        /// <summary>Presses a command button: TRUE, then FALSE after the pulse length. Faults when it cannot be written.</summary>
        Task PulseAsync(int device, string key);

        /// <summary>Sets a switch the HMI holds (TRUE stays TRUE until set FALSE). Faults when it cannot be written.</summary>
        Task SetFlagAsync(int device, string key, bool on);

        /// <summary>
        /// Writes values in the HMI's units (multiplied by each key's scale for the PLC) and
        /// reads each back. Faults, listing every value that was not written or not kept.
        /// </summary>
        Task WriteAsync(int device, IReadOnlyDictionary<string, double> values);
    }

    /// <summary>The third-party devices of the Manual tab.</summary>
    public interface IThirdPartyDevices
    {
        IPlcDeviceService Dispensers { get; }

        IPlcDeviceService WeighingScales { get; }

        IPlcDeviceService VibrationSensors { get; }
    }
}
