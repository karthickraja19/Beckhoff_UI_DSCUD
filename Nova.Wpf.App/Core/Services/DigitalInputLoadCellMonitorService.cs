using System;
using System.Collections.Generic;
using System.Linq;
using Nova.Abstractions.Hardware;
using Nova.Abstractions.Services;

namespace Nova.Core.Services
{
    /// <summary>
    /// Loadcell monitor backed by digital inputs. Until analog wiring is added,
    /// each channel reports 1.00 kg when its input is ON and 0.00 kg when OFF.
    /// </summary>
    public sealed class DigitalInputLoadCellMonitorService : ILoadCellMonitorService, IDisposable
    {
        private readonly object _sync = new object();
        private readonly IDigitalIOService _digitalIO;
        private readonly List<LoadCellChannel> _channels;
        private bool _started;

        public event EventHandler<LoadCellStatusSnapshot> LoadCellStatusUpdated;

        public DigitalInputLoadCellMonitorService(IDigitalIOService digitalIO)
        {
            _digitalIO = digitalIO ?? throw new ArgumentNullException(nameof(digitalIO));
            _channels = new List<LoadCellChannel>
            {
                new LoadCellChannel("Robot_LoadCell", new []
                {
                    "Robot_LoadCell",
                    "AI0_0_Robot_Load_Cell",
                    "Robot_Load_Cell",
                }),
                new LoadCellChannel("Pressing_LoadCell", new []
                {
                    "Pressing_LoadCell",
                    "AI0_1_Roller_Module_Load_Cell_1",
                    "Roller_Module_Load_Cell_1",
                    "AI0_2_Roller_Module_Load_Cell_2",
                    "Roller_Module_Load_Cell_2",
                }),
            };
        }

        public IReadOnlyList<LoadCellStatusSnapshot> ReadAll()
        {
            lock (_sync)
            {
                return _channels.Select(channel => channel.ToSnapshot()).ToList();
            }
        }

        public void Start()
        {
            lock (_sync)
            {
                if (_started)
                {
                    return;
                }

                _started = true;
                ResolveInputNames();
                _digitalIO.IOChanged += OnIoChanged;
            }

            RefreshFromInputs(forcePublish: true);
        }

        public void Stop()
        {
            lock (_sync)
            {
                if (!_started)
                {
                    return;
                }

                _started = false;
                _digitalIO.IOChanged -= OnIoChanged;
            }
        }

        public void Dispose() => Stop();

        private void OnIoChanged(object sender, EventArgs e)
            => RefreshFromInputs(forcePublish: false);

        private void ResolveInputNames()
        {
            var inputs = _digitalIO.ReadInputs();
            var availableNames = new HashSet<string>(
                inputs.Select(point => point.Name ?? string.Empty),
                StringComparer.OrdinalIgnoreCase);

            foreach (var channel in _channels)
            {
                string resolved = channel.Aliases.FirstOrDefault(alias => availableNames.Contains(alias));
                channel.ResolvedInputName = resolved ?? string.Empty;
            }
        }

        private void RefreshFromInputs(bool forcePublish)
        {
            List<LoadCellStatusSnapshot> updates = new List<LoadCellStatusSnapshot>();

            lock (_sync)
            {
                if (!_started)
                {
                    return;
                }

                foreach (var channel in _channels)
                {
                    double newValue = 0d;
                    if (!string.IsNullOrWhiteSpace(channel.ResolvedInputName))
                    {
                        bool isOn = _digitalIO.ReadInput(channel.ResolvedInputName);
                        newValue = isOn ? 1d : 0d;
                    }

                    if (forcePublish || Math.Abs(channel.Value - newValue) > 0.0001d)
                    {
                        channel.Value = newValue;
                        updates.Add(channel.ToSnapshot());
                    }
                }
            }

            foreach (var snapshot in updates)
            {
                LoadCellStatusUpdated?.Invoke(this, snapshot);
            }
        }

        private sealed class LoadCellChannel
        {
            public string ComponentId { get; }
            public IReadOnlyList<string> Aliases { get; }
            public string ResolvedInputName { get; set; }
            public double Value { get; set; }

            public LoadCellChannel(string componentId, IEnumerable<string> aliases)
            {
                ComponentId = componentId;
                Aliases = aliases
                    .Where(alias => !string.IsNullOrWhiteSpace(alias))
                    .ToList();
                ResolvedInputName = string.Empty;
                Value = 0d;
            }

            public LoadCellStatusSnapshot ToSnapshot()
            {
                return new LoadCellStatusSnapshot
                {
                    ComponentId = ComponentId,
                    Value = Value,
                };
            }
        }
    }
}
