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
    }
}
