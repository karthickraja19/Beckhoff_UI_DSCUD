using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Nova.Abstractions.Hardware;
using Nova.Abstractions.Services;

namespace Nova.Core.Services
{
    /// <summary>
    /// Development stand-in for live loadcell telemetry.
    /// </summary>
    public sealed class SimulatedLoadCellMonitorService : ILoadCellMonitorService, IDisposable
    {
        private static readonly TimeSpan Period = TimeSpan.FromMilliseconds(250);

        private readonly object _sync = new object();
        private readonly List<LoadCellStatusSnapshot> _channels;
        private readonly Random _random = new Random();
        private Timer _timer;
        private int _polling;

        public event EventHandler<LoadCellStatusSnapshot> LoadCellStatusUpdated;

        public SimulatedLoadCellMonitorService()
        {
            _channels = new List<LoadCellStatusSnapshot>
            {
                new LoadCellStatusSnapshot { ComponentId = "Robot_LoadCell", Value = 0d },
                new LoadCellStatusSnapshot { ComponentId = "Pressing_LoadCell", Value = 0d },
            };
        }

        public IReadOnlyList<LoadCellStatusSnapshot> ReadAll()
        {
            lock (_sync)
            {
                return _channels.Select(Clone).ToList();
            }
        }

        public void Start()
        {
            lock (_sync)
            {
                if (_timer == null)
                {
                    _timer = new Timer(OnTick, null, Period, Period);
                }
            }
        }

        public void Stop()
        {
            lock (_sync)
            {
                _timer?.Dispose();
                _timer = null;
            }
        }

        public void Dispose() => Stop();

        private void OnTick(object state)
        {
            if (Interlocked.Exchange(ref _polling, 1) == 1)
            {
                return;
            }

            try
            {
                List<LoadCellStatusSnapshot> updates;
                lock (_sync)
                {
                    Advance(0, 0d, 4d, 0.20d);
                    Advance(1, 0d, 10d, 0.35d);
                    updates = _channels.Select(Clone).ToList();
                }

                foreach (var snapshot in updates)
                {
                    LoadCellStatusUpdated?.Invoke(this, snapshot);
                }
            }
            finally
            {
                Interlocked.Exchange(ref _polling, 0);
            }
        }

        private void Advance(int index, double min, double max, double step)
        {
            double delta = (_random.NextDouble() * 2d - 1d) * step;
            double next = _channels[index].Value + delta;
            _channels[index].Value = Clamp(next, min, max);
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }

        private static LoadCellStatusSnapshot Clone(LoadCellStatusSnapshot snapshot)
        {
            return new LoadCellStatusSnapshot
            {
                ComponentId = snapshot.ComponentId,
                Value = snapshot.Value,
            };
        }
    }
}
