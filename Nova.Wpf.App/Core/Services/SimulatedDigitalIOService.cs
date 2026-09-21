using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nova.Abstractions.Hardware;
using Nova.Abstractions.Services;

namespace Nova.Core.Services
{
    /// <summary>
    /// Development stand-in for the machine's digital IO: point definitions come
    /// from the same Config.csv the machine uses (supplied by the composition
    /// root), inputs idle at off, and output writes latch immediately — enough to
    /// exercise the IO tab and IO-driven devices without a card.
    /// </summary>
    public sealed class SimulatedDigitalIOService : IDigitalIOService
    {
        private readonly object _sync = new object();
        private readonly List<IOPointSnapshot> _inputs;
        private readonly List<IOPointSnapshot> _outputs;
        private readonly Dictionary<string, IOPointSnapshot> _outputsByName;

        public event EventHandler IOChanged;

        public SimulatedDigitalIOService(IEnumerable<IOPointSnapshot> inputs, IEnumerable<IOPointSnapshot> outputs)
        {
            _inputs = inputs.OrderBy(p => p.Bit).ToList();
            _outputs = outputs.OrderBy(p => p.Bit).ToList();
            _outputsByName = _outputs
                .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyList<IOPointSnapshot> ReadInputs()
        {
            lock (_sync) return _inputs.Select(Clone).ToList();
        }

        public IReadOnlyList<IOPointSnapshot> ReadOutputs()
        {
            lock (_sync) return _outputs.Select(Clone).ToList();
        }

        public bool ReadInput(string name)
        {
            lock (_sync) return _inputs.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))?.IsOn ?? false;
        }

        public bool ReadOutput(string name)
        {
            lock (_sync) return _outputsByName.TryGetValue(name ?? "", out var p) && p.IsOn;
        }

        public Task<bool> WriteOutputAsync(string name, bool on)
        {
            lock (_sync)
            {
                if (!_outputsByName.TryGetValue(name ?? "", out var point))
                    return Task.FromResult(false);
                point.IsOn = on;
            }
            IOChanged?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(true);
        }

        public Task<bool> WriteOutputAsync(int bit, bool on)
        {
            lock (_sync)
            {
                var point = _outputs.FirstOrDefault(p => p.Bit == bit);
                if (point == null)
                    return Task.FromResult(false);
                point.IsOn = on;
            }
            IOChanged?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(true);
        }

        public bool[] GetAllInputs()
        {
            lock (_sync)
            {
                int max = _inputs.Count == 0 ? 0 : _inputs.Max(p => p.Bit) + 1;
                var arr = new bool[max];
                foreach (var p in _inputs)
                    arr[p.Bit] = p.IsOn;
                return arr;
            }
        }

        public void Start() { }
        public void Stop() { }

        private static IOPointSnapshot Clone(IOPointSnapshot p)
            => new IOPointSnapshot { Bit = p.Bit, Name = p.Name, Address = p.Address, IsOn = p.IsOn };
    }
}
