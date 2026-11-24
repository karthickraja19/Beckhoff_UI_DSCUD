using System.Collections.Generic;
using Nova.Abstractions.Machine.Enums;
using Nova.Abstractions.Services;

namespace Nova.Core.Services
{
    /// <summary>
    /// Mode, bypass and maintenance-station state held by the HMI for the Device Settings tab.
    /// The PLC owns the machine's run state; nothing here is written to the PLC yet.
    /// </summary>
    public sealed class MachineContext : IMachineContext
    {
        private readonly Dictionary<DeviceBypassKind, bool> _bypasses = new Dictionary<DeviceBypassKind, bool>();

        public OperationMode MachineMode { get; set; }

        /// <summary>Set from App.config's MasterCardType at startup.</summary>
        public string MasterCardType { get; set; } = string.Empty;

        public IReadOnlyCollection<int> MaintenanceStations { get; set; } = new int[0];

        public bool GetBypass(DeviceBypassKind kind)
        {
            bool value;
            return _bypasses.TryGetValue(kind, out value) && value;
        }

        public void SetBypass(DeviceBypassKind kind, bool value)
        {
            _bypasses[kind] = value;
        }
    }
}
