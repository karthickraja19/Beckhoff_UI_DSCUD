using System;
using System.Threading.Tasks;
using Nova.Abstractions.Machine.Enums;

namespace Nova.Abstractions.Machine.Interfaces
{
    public interface IMachineOrchestrator
    {
        MachineStatus CurrentStatus { get; }
        bool IsHomingDone { get; }
        bool IsAutoRunning { get; }

        event Action<MachineStatus> OnMachineStatusChanged;

        Task StartAutoAsync();
        void Stop();
        void Pause();
        void Resume();
        Task StartHomingAsync();
        void ResetAlarms();

        /// <summary>
        /// Holds the machine in manual mode, or lets it go (the Setting page's Manual and
        /// Calibration tabs, the Recipe page's Gantry tab). Each caller pairs a true with a false; the machine stays in manual
        /// mode while any caller holds it.
        /// </summary>
        void SetManualMode(bool on);
    }
}
