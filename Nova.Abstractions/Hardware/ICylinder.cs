using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nova.Abstractions.Hardware
{
    /// <summary>
    /// One pneumatic cylinder as the Manual screen drives it. Extend/Retract mirror
    /// V3's manual-mode buttons: fire the valve and confirm against the reed sensor
    /// (bounded by the configured timeout), or fire without sense when no reed is
    /// configured. No auto-cycle/sequencing here — that lives in the sequencer.
    /// Address lists are for display; <see cref="ReadStatus"/> is polled off the IO thread.
    /// </summary>
    public interface ICylinder
    {
        string ComponentId { get; }
        string CylinderName { get; }

        string ExtendOutputAddress { get; }
        string RetractOutputAddress { get; }
        IReadOnlyList<string> ExtendSenseAddresses { get; }
        IReadOnlyList<string> RetractSenseAddresses { get; }

        Task<bool> ExtendAsync();
        Task<bool> RetractAsync();
        Task<bool> StopAsync();

        CylinderStatusSnapshot ReadStatus();
    }
}
