using System.Threading;
using Nova.Abstractions.Machine.Enums;

namespace Nova.Abstractions.Services
{
    /// <summary>
    /// Holds the global state of the machine, replacing the legacy static GlobalClass.
    /// Injected as a Singleton so all sequences and UI components share the exact same state.
    /// </summary>
    public interface IMachineContext
    {
        // --- Sequence Synchronization ---
        ManualResetEventSlim PauseEvent { get; }
        ManualResetEvent PauseHomingEvent { get; }

        // --- Core Machine State ---
        MachineStatus CurrentStatus { get; set; }
        OperationMode MachineMode { get; set; }

        string MasterCardType { get; set; }

        bool IsHomingDone { get; set; }
        bool IsHomingStopped { get; set; }
        bool IsAppIdle { get; set; }
        bool HasActiveAlarm { get; set; }
        bool CardInitSuccess { get; set; }

        // --- Hardware Bypass Toggles ---
        bool SafetyDoorBypass { get; set; }
        bool ScannerBypass { get; set; }
        bool BuzzerBypass { get; set; }
        bool SfcPdcaBypass { get; set; }
        bool GetBypass(DeviceBypassKind kind);
        void SetBypass(DeviceBypassKind kind, bool value);

        // --- High-Level Control ---
        void RequestPause();
        void RequestResume();
        void RequestStop();
    }
}
