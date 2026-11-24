using System.Collections.Generic;
using System.Linq;
using Nova.Abstractions.Hardware;
using Nova.Abstractions.Services;

namespace Nova.Core.Services
{
    /// <summary>
    /// Registry over whatever cylinder set the composition root supplies —
    /// simulated on a dev PC, legacy CylinderHelperLib adapters on the machine.
    /// Replaces the V3 cylinder object dictionary.
    /// </summary>
    public sealed class CylinderService : ICylinderService
    {
        private readonly List<ICylinder> _cylinders;
        private readonly Dictionary<string, ICylinder> _byId;

        public CylinderService(IEnumerable<ICylinder> cylinders)
        {
            _cylinders = cylinders.ToList();
            _byId = _cylinders.ToDictionary(c => c.ComponentId);
        }

        public IEnumerable<ICylinder> GetAllCylinders() => _cylinders;

        public ICylinder GetCylinder(string componentId)
            => componentId != null && _byId.TryGetValue(componentId, out var c) ? c : null;
    }
}
