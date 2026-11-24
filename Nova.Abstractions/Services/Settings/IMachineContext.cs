using System.Collections.Generic;
using Nova.Abstractions.Machine.Enums;

namespace Nova.Abstractions.Services
{
    /// <summary>
    /// The operation mode, device bypasses and maintenance stations the Device Settings tab
    /// edits, shared as a singleton. The PLC owns the machine's run state
    /// (<see cref="Nova.Abstractions.Machine.Interfaces.IMachineOrchestrator"/> reads it back).
    /// </summary>
    public interface IMachineContext
    {
        OperationMode MachineMode { get; set; }

        /// <summary>Controller family, from App.config's MasterCardType.</summary>
        string MasterCardType { get; set; }

        bool GetBypass(DeviceBypassKind kind);
        void SetBypass(DeviceBypassKind kind, bool value);

        /// <summary>The stations (1-based) under maintenance while MachineMode is Maintenance; empty otherwise.</summary>
        IReadOnlyCollection<int> MaintenanceStations { get; set; }
    }
}
