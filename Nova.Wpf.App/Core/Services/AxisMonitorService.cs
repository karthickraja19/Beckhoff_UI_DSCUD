using System;
using System.Threading;
using Nova.Abstractions.Hardware;
using Nova.Abstractions.Services;

namespace Nova.Core.Services
{
    /// <summary>
    /// Polls every registered axis AND stepper on a background timer and publishes
    /// one status snapshot per device per cycle (steppers reuse the axis snapshot
    /// shape). This is the machine-wide replacement for the per-form WinForms
    /// timers in V3 (ManualScreen.timer1_Tick etc.).
    /// </summary>
    public sealed class AxisMonitorService : IAxisMonitorService, IDisposable
    {
        private static readonly TimeSpan Period = TimeSpan.FromMilliseconds(250);

        private readonly IAxisService _axisService;
        private readonly IStepperService _stepperService;
        private readonly object _sync = new object();
        private Timer _timer;
        private int _polling;

        public event EventHandler<AxisStatusSnapshot> AxisStatusUpdated;

        public AxisMonitorService(IAxisService axisService, IStepperService stepperService = null)
        {
            _axisService = axisService;
            _stepperService = stepperService;
        }

        public void Start()
        {
            lock (_sync)
            {
                if (_timer == null)
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
            // Skip a cycle rather than stacking up if a hardware read runs long.
            if (Interlocked.Exchange(ref _polling, 1) == 1)
                return;

            try
            {
                foreach (var axis in _axisService.GetAllAxes())
                {
                    var snapshot = axis.ReadStatus();
                    if (snapshot != null)
                        AxisStatusUpdated?.Invoke(this, snapshot);
                }

                if (_stepperService != null)
                {
                    foreach (var stepper in _stepperService.GetAllSteppers())
                    {
                        var snapshot = stepper.ReadStatus();
                        if (snapshot != null)
                            AxisStatusUpdated?.Invoke(this, snapshot);
                    }
                }
            }
            catch
            {
                // Implementations should not throw from ReadStatus; never let a
                // faulty read kill the monitor timer.
            }
            finally
            {
                Interlocked.Exchange(ref _polling, 0);
            }
        }
    }
}
