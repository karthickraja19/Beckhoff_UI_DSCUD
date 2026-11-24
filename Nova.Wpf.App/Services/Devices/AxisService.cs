using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nova.Abstractions.Hardware;
using Nova.Abstractions.Services;

namespace Nova.Core.Services
{
    /// <summary>
    /// Registry over whatever axis set the composition root supplies — simulated
    /// axes on a dev PC, legacy Advantech adapters on the machine. Replaces the V3
    /// UIGlobalVariable.MotorObj static dictionary.
    /// </summary>
    public sealed class AxisService : IAxisService
    {
        private readonly List<IAxis> _axes;
        private readonly Dictionary<string, IAxis> _byId;

        public AxisService(IEnumerable<IAxis> axes)
        {
            _axes = axes.ToList();
            _byId = _axes.ToDictionary(a => a.ComponentId);
        }

        public IAxis GetAxis(string componentId)
            => componentId != null && _byId.TryGetValue(componentId, out var axis) ? axis : null;

        public IReadOnlyList<IAxis> GetAllAxes() => _axes;

        public async Task EnableAllAsync()
        {
            foreach (var axis in _axes)
                await axis.EnableAsync();
        }

        public async Task DisableAllAsync()
        {
            foreach (var axis in _axes)
                await axis.DisableAsync();
        }

        public async Task EmergencyStopAllAsync()
        {
            foreach (var axis in _axes)
                await axis.StopAsync();
        }
    }
}
