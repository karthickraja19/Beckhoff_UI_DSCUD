using System.Threading.Tasks;
using Nova.Abstractions.Hardware;

namespace Nova.Core.Hardware
{
    /// <summary>
    /// Development stand-in for a vacuum unit: Vacuum/Blow/Off latch the commanded
    /// state, and feedback follows the vacuum command so the tab shows a plausible
    /// "vacuum achieved" indication without hardware.
    /// </summary>
    public sealed class SimulatedVacuum : IVacuum
    {
        private readonly object _sync = new object();
        private bool _vacuum;
        private bool _blow;

        public string ComponentId { get; }
        public string VacuumName { get; }
        public string FeedbackAddress { get; }

        public SimulatedVacuum(string componentId, string vacuumName, string feedbackAddress)
        {
            ComponentId = componentId;
            VacuumName = vacuumName ?? componentId;
            FeedbackAddress = feedbackAddress ?? "";
        }

        public Task<bool> VacuumOnAsync()
        {
            lock (_sync) { _vacuum = true; _blow = false; }
            return Task.FromResult(true);
        }

        public Task<bool> BlowOnAsync()
        {
            lock (_sync) { _vacuum = false; _blow = true; }
            return Task.FromResult(true);
        }

        public Task<bool> OffAsync()
        {
            lock (_sync) { _vacuum = false; _blow = false; }
            return Task.FromResult(true);
        }

        public VacuumStatusSnapshot ReadStatus()
        {
            lock (_sync)
            {
                return new VacuumStatusSnapshot
                {
                    ComponentId = ComponentId,
                    FeedbackOn = _vacuum,
                    VacuumCommanded = _vacuum,
                    BlowCommanded = _blow,
                };
            }
        }
    }
}
