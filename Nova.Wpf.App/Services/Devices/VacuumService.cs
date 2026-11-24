using System.Collections.Generic;
using System.Linq;
using Nova.Abstractions.Hardware;
using Nova.Abstractions.Services;

namespace Nova.Core.Services
{
    /// <summary>
    /// Registry over whatever vacuum set the composition root supplies — simulated
    /// on a dev PC, legacy VacuumHelperLib adapters on the machine.
    /// </summary>
    public sealed class VacuumService : IVacuumService
    {
        private readonly List<IVacuum> _vacuums;
        private readonly Dictionary<string, IVacuum> _byId;

        public VacuumService(IEnumerable<IVacuum> vacuums)
        {
            _vacuums = vacuums.ToList();
            _byId = _vacuums.ToDictionary(v => v.ComponentId);
        }

        public IEnumerable<IVacuum> GetAllVacuums() => _vacuums;

        public IVacuum GetVacuum(string componentId)
            => componentId != null && _byId.TryGetValue(componentId, out var v) ? v : null;
    }
}
