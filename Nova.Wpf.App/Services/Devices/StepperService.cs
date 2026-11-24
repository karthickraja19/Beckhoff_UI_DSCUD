using System.Collections.Generic;
using System.Linq;
using Nova.Abstractions.Hardware;
using Nova.Abstractions.Services;

namespace Nova.Core.Services
{
    /// <summary>
    /// Registry over whatever stepper set the composition root supplies —
    /// simulated on a dev PC, TwinCAT adapters over Master.Configuration's
    /// StepperObjects on the machine.
    /// </summary>
    public sealed class StepperService : IStepperService
    {
        private readonly List<IStepper> _steppers;
        private readonly Dictionary<string, IStepper> _byId;

        public StepperService(IEnumerable<IStepper> steppers)
        {
            _steppers = steppers.ToList();
            _byId = _steppers.ToDictionary(s => s.ComponentId);
        }

        public IStepper GetStepper(string componentId)
            => componentId != null && _byId.TryGetValue(componentId, out var stepper) ? stepper : null;

        public IReadOnlyList<IStepper> GetAllSteppers() => _steppers;
    }
}
