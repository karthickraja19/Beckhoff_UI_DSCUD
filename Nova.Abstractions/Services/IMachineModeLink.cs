using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nova.Abstractions.Machine.Enums;

namespace Nova.Abstractions.Services
{
    /// <summary>What the PLC reports for Config &gt; Device Settings, as it stands now.</summary>
    public sealed class MachineModeState
    {
        /// <summary>False while the PLC is not there; nothing below means anything then.</summary>
        public bool IsLive { get; set; }

        /// <summary>Why <see cref="IsLive"/> is false; empty while live.</summary>
        public string NotLiveReason { get; set; } = string.Empty;

        /// <summary>The run mode the PLC is in, as an HMI mode; null when it reports none the HMI knows.</summary>
        public OperationMode? Mode { get; set; }

        /// <summary>The PLC's own run mode number, or null when it does not report one.</summary>
        public int? PlcModeNumber { get; set; }

        /// <summary>Whether the PLC takes a mode change now; null when it does not say.</summary>
        public bool? ChangeAllowed { get; set; }

        /// <summary>Each bypass the PLC has, with its value. A bypass the PLC does not have is left out.</summary>
        public IDictionary<DeviceBypassKind, bool> Bypasses { get; set; } = new Dictionary<DeviceBypassKind, bool>();

        /// <summary>The stations (1-based) the PLC holds out of production.</summary>
        public IReadOnlyCollection<int> MaintenanceStations { get; set; } = new int[0];
    }

    /// <summary>What Device Settings asks the PLC for on Save.</summary>
    public sealed class MachineModeRequest
    {
        public OperationMode Mode { get; set; }

        /// <summary>Only the bypasses to write; one left out is left as it is.</summary>
        public IDictionary<DeviceBypassKind, bool> Bypasses { get; set; } = new Dictionary<DeviceBypassKind, bool>();

        /// <summary>The stations (1-based) to hold out of production; every other station is put back in.</summary>
        public IReadOnlyCollection<int> MaintenanceStations { get; set; } = new int[0];
    }

    /// <summary>What the PLC made of a <see cref="MachineModeRequest"/>.</summary>
    public sealed class MachineModeResult
    {
        /// <summary>True when everything asked for was written and read back as written.</summary>
        public bool Success => Problems.Count == 0;

        /// <summary>What was not taken, one line each, in the operator's words.</summary>
        public IList<string> Problems { get; } = new List<string>();
    }

    /// <summary>
    /// The PLC side of Config &gt; Device Settings: the machine's run mode, its bypass
    /// switches and the stations held out for maintenance. The PLC owns all three - the
    /// HMI asks, and shows what the PLC reports back.
    /// </summary>
    public interface IMachineModeLink
    {
        /// <summary>True when the PLC has a run mode behind this HMI mode (or, for Maintenance, a station to hold out).</summary>
        bool CanSelect(OperationMode mode);

        /// <summary>Why <see cref="CanSelect"/> is false, for the operator; empty otherwise.</summary>
        string WhyNotSelectable(OperationMode mode);

        /// <summary>True when the PLC has a bypass behind this switch.</summary>
        bool CanSet(DeviceBypassKind kind);

        /// <summary>True when the PLC can hold this station (1-based) out of production.</summary>
        bool CanHoldOut(int station);

        /// <summary>What the PLC reports now. Never null.</summary>
        MachineModeState Read();

        /// <summary><see cref="Read"/> would give something else. Raised on any thread.</summary>
        event EventHandler Changed;

        /// <summary>
        /// Writes the request to the PLC and reads it back. Never throws: what could not be
        /// written, or was not taken, is in the result.
        /// </summary>
        Task<MachineModeResult> ApplyAsync(MachineModeRequest request);
    }
}
