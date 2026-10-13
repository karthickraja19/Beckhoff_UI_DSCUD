using System.Threading;
using Nova.Abstractions.Machine.Enums;
using Nova.Abstractions.Services;

namespace Nova.Core.Services
{
    /// <summary>
    /// Machine state held by the HMI. The PLC owns the real state; until the ADS adapters
    /// exist this is the store the orchestrator and the Settings screen share. Mode and
    /// bypass writes become PLC writes at that point.
    /// </summary>
    public sealed class MachineContext : IMachineContext
    {
        public ManualResetEventSlim PauseEvent { get; } = new ManualResetEventSlim(true);

        public ManualResetEvent PauseHomingEvent { get; } = new ManualResetEvent(true);

        public MachineStatus CurrentStatus { get; set; }

        public OperationMode MachineMode { get; set; }

        /// <summary>Set from App.config's MasterCardType at startup.</summary>
        public string MasterCardType { get; set; } = string.Empty;

        public bool IsHomingDone { get; set; }

        public bool IsHomingStopped { get; set; }

        public bool IsAppIdle { get; set; }

        public bool HasActiveAlarm { get; set; }

        public bool CardInitSuccess { get; set; }

        public bool SafetyDoorBypass { get; set; }

        public bool ScannerBypass { get; set; }

        public bool BuzzerBypass { get; set; }

        public bool SfcPdcaBypass { get; set; }

        public bool GetBypass(DeviceBypassKind kind)
        {
            switch (kind)
            {
                case DeviceBypassKind.SafetyDoor: return SafetyDoorBypass;
                case DeviceBypassKind.Scanner: return ScannerBypass;
                case DeviceBypassKind.Buzzer: return BuzzerBypass;
                case DeviceBypassKind.SfcPdca: return SfcPdcaBypass;
                default: return false;
            }
        }

        public void SetBypass(DeviceBypassKind kind, bool value)
        {
            switch (kind)
            {
                case DeviceBypassKind.SafetyDoor: SafetyDoorBypass = value; break;
                case DeviceBypassKind.Scanner: ScannerBypass = value; break;
                case DeviceBypassKind.Buzzer: BuzzerBypass = value; break;
                case DeviceBypassKind.SfcPdca: SfcPdcaBypass = value; break;
            }
        }

        // Raw flag writes only - machine control from the UI goes through IMachineOrchestrator.
        public void RequestPause()
        {
            PauseEvent.Reset();
            CurrentStatus = MachineStatus.Pause;
        }

        public void RequestResume()
        {
            PauseEvent.Set();
            if (CurrentStatus == MachineStatus.Pause)
            {
                CurrentStatus = MachineStatus.Running;
            }
        }

        public void RequestStop()
        {
            PauseEvent.Set();
            PauseHomingEvent.Set();
            CurrentStatus = MachineStatus.Stop;
            IsAppIdle = true;
        }
    }
}
