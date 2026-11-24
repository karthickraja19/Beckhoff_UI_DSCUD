using System.Collections.Generic;
using System.Threading.Tasks;
using Nova.Abstractions.Hardware;

namespace Nova.Abstractions.Services
{
    /// <summary>
    /// The global orchestrator for all axes in the machine.
    /// This is injected into ViewModels and State Machines to command motion.
    /// </summary>
    public interface IAxisService
    {
        /// <summary>
        /// Retrieves a specific axis by its unique component ID (e.g. "Axis_X")
        /// </summary>
        IAxis GetAxis(string componentId);
        
        /// <summary>
        /// Retrieves all registered axes for UI binding (e.g. Dashboard)
        /// </summary>
        IReadOnlyList<IAxis> GetAllAxes();

        Task EnableAllAsync();
        Task DisableAllAsync();
        Task EmergencyStopAllAsync();
    }
}
