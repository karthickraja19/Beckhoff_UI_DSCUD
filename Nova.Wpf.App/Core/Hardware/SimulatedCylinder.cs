using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nova.Abstractions.Hardware;

namespace Nova.Core.Hardware
{
    /// <summary>
    /// Development stand-in for a cylinder: Extend/Retract flip the sensed side
    /// immediately, so the Manual state board (piston animation, reed chips,
    /// counts) can be exercised without air or IO.
    /// </summary>
    public sealed class SimulatedCylinder : ICylinder
    {
        private readonly object _sync = new object();
        private bool _extended;

        public string ComponentId { get; }
        public string CylinderName { get; }
        public string ExtendOutputAddress { get; }
        public string RetractOutputAddress { get; }
        public IReadOnlyList<string> ExtendSenseAddresses { get; }
        public IReadOnlyList<string> RetractSenseAddresses { get; }

        public SimulatedCylinder(string componentId, string cylinderName,
            string extendOutputAddress, string retractOutputAddress,
            IEnumerable<string> extendSenseAddresses, IEnumerable<string> retractSenseAddresses,
            bool startExtended = false)
        {
            ComponentId = componentId;
            CylinderName = cylinderName ?? componentId;
            ExtendOutputAddress = extendOutputAddress ?? "";
            RetractOutputAddress = retractOutputAddress ?? "";
            ExtendSenseAddresses = (extendSenseAddresses ?? Enumerable.Empty<string>()).ToList();
            RetractSenseAddresses = (retractSenseAddresses ?? Enumerable.Empty<string>()).ToList();
            _extended = startExtended;
        }

        public Task<bool> ExtendAsync()
        {
            lock (_sync) _extended = true;
            return Task.FromResult(true);
        }

        public Task<bool> RetractAsync()
        {
            lock (_sync) _extended = false;
            return Task.FromResult(true);
        }

        public Task<bool> StopAsync() => Task.FromResult(true);

        public CylinderStatusSnapshot ReadStatus()
        {
            lock (_sync)
            {
                return new CylinderStatusSnapshot
                {
                    ComponentId = ComponentId,
                    IsExtended = _extended,
                    IsRetracted = !_extended,
                    IsBothSensed = false,
                    SafeToExtend = true,
                    SafeToRetract = true,
                    ExtendSenseStates = ExtendSenseAddresses.Select(_ => _extended).ToArray(),
                    RetractSenseStates = RetractSenseAddresses.Select(_ => !_extended).ToArray(),
                };
            }
        }
    }
}
