using System.Collections.Generic;
using Nova.Abstractions.Hardware.Interfaces;

namespace Nova.Abstractions.Hardware
{
    /// <summary>
    /// Central registry for all hardware components in the machine.
    /// Sequences inject this registry to look up axes, cylinders, and sensors by their Component ID.
    /// Replaces the legacy static 'ObjectList'.
    /// </summary>
    public interface IDeviceRegistry
    {
        // --- Fetch Methods (Used by Sequences) ---
        IAxis GetAxis(string componentId);
        ICylinder GetCylinder(string componentId);
        IVacuum GetVacuum(string componentId);
        IStepper GetStepper(string componentId);
        
        // Modules from other teams
        INetworkProtocol GetVision(string componentId);
        INetworkProtocol GetRobot(string componentId);
        INetworkProtocol GetScanner(string componentId);

        // --- Registration Methods (Used by Machine Bootstrapper) ---
        void RegisterAxis(string componentId, IAxis axis);
        void RegisterCylinder(string componentId, ICylinder cylinder);
        void RegisterVacuum(string componentId, IVacuum vacuum);
        void RegisterStepper(string componentId, IStepper stepper);
        void RegisterVision(string componentId, INetworkProtocol vision);
        void RegisterRobot(string componentId, INetworkProtocol robot);
        void RegisterScanner(string componentId, INetworkProtocol scanner);
        
        /// <summary>
        /// Retrieves a list of all registered component IDs for a specific category.
        /// Useful for UI diagnostics or machine initialization loops.
        /// </summary>
        IEnumerable<string> GetRegisteredAxes();
        IEnumerable<string> GetRegisteredCylinders();
        IEnumerable<string> GetRegisteredVacuums();
    }
}
