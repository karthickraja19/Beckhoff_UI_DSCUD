using System;
using System.Threading;
using Nova.Abstractions.Machine.Models;
using Nova.Abstractions.Services;

namespace Nova.Core.Services
{
    /// <summary>
    /// Development stand-in for the machine engine: fabricates plausible shift and
    /// health numbers on a 1 s cadence so the Home screen live-binding path can be
    /// exercised without hardware. On the real machine, replace this DI registration
    /// with an implementation fed by the V3 engine (Master.Scheduler
    /// ShiftStatusRefresher / IShiftService) — the UI needs no changes.
    /// </summary>
    public sealed class SimulatedMachineTelemetryService : IMachineTelemetryService, IDisposable
    {
        private static readonly TimeSpan Period = TimeSpan.FromSeconds(1);

        private readonly object _sync = new object();
        private readonly Random _random = new Random();
        private Timer _timer;

        // Accumulated machine state, advanced once per tick.
        private double _runSeconds;
        private double _idleSeconds;
        private double _downSeconds;
        private int _passCount;
        private int _failCount;
        private double _cycleSeconds = 18.5;
        private double _secondsIntoCycle;

        public MachineTelemetrySnapshot Latest { get; private set; }

        public event EventHandler<MachineTelemetrySnapshot> TelemetryUpdated;

        public void Start()
        {
            lock (_sync)
            {
                if (_timer != null)
                    return;

                Latest = BuildSnapshot();
                _timer = new Timer(OnTick, null, Period, Period);
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
            MachineTelemetrySnapshot snapshot;
            lock (_sync)
            {
                Advance();
                snapshot = BuildSnapshot();
                Latest = snapshot;
            }

            TelemetryUpdated?.Invoke(this, snapshot);
        }

        /// <summary>Moves the simulated machine forward by one period.</summary>
        private void Advance()
        {
            double roll = _random.NextDouble();
            if (roll < 0.90)
            {
                _runSeconds += Period.TotalSeconds;
                _secondsIntoCycle += Period.TotalSeconds;
            }
            else if (roll < 0.98)
            {
                _idleSeconds += Period.TotalSeconds;
            }
            else
            {
                _downSeconds += Period.TotalSeconds;
            }

            if (_secondsIntoCycle >= _cycleSeconds)
            {
                _secondsIntoCycle = 0;
                _cycleSeconds = 17.5 + _random.NextDouble() * 2.0;

                if (_random.NextDouble() < 0.98)
                    _passCount++;
                else
                    _failCount++;
            }
        }

        private MachineTelemetrySnapshot BuildSnapshot()
        {
            int overall = _passCount + _failCount;
            double yield = overall > 0 ? _passCount * 100.0 / overall : 100.0;
            double runHours = _runSeconds / 3600.0;

            return new MachineTelemetrySnapshot
            {
                ShiftName = DateTime.Now.Hour >= 6 && DateTime.Now.Hour < 18 ? "Day Shift" : "Night Shift",
                MachineMode = "Auto",
                RunTimeSeconds = _runSeconds,
                IdleTimeSeconds = _idleSeconds,
                DownTimeSeconds = _downSeconds,
                PassCount = _passCount,
                FailCount = _failCount,
                CycleTimeSeconds = _cycleSeconds,
                Uph = (int)(3600.0 / _cycleSeconds * (yield / 100.0)),
                YieldPercent = yield,
                OeePercent = Math.Min(100.0, 84.0 + _random.NextDouble() * 5.0),
                RetestRatePercent = 2.0 + _random.NextDouble(),
                TossingRatePercent = 1.5 + _random.NextDouble() * 0.6,
                MtbfHours = _failCount > 0 ? runHours / _failCount : runHours,
                MtbaHours = _failCount > 0 ? runHours / (_failCount * 2.0) : runHours,
                LastUpdated = DateTime.Now,
            };
        }
    }
}
