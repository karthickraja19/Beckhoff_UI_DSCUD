using System.Threading.Tasks;
using Nova.Abstractions.Hardware;

namespace Nova.Core.Hardware
{
    /// <summary>
    /// Development stand-in for a stepper/conveyor: Forward/Reverse just latch a
    /// running state and speed until Stop, so the Stepper tab's chips and live
    /// values can be exercised without hardware.
    /// </summary>
    public sealed class SimulatedStepper : IStepper
    {
        private const double DefaultSpeed = 25;

        private readonly object _sync = new object();
        private bool _enabled = true;
        private bool _alarm;
        private int _direction;      // -1, 0, +1
        private double _speed;

        public string ComponentId { get; }
        public bool IsAxisBased { get; }

        public SimulatedStepper(string componentId, bool isAxisBased)
        {
            ComponentId = componentId;
            IsAxisBased = isAxisBased;
        }

        public Task<bool> EnableAsync()
        {
            lock (_sync) _enabled = true;
            return Task.FromResult(true);
        }

        public Task<bool> ForwardAsync(double speed)
        {
            lock (_sync)
            {
                if (!_enabled || _alarm) return Task.FromResult(false);
                _direction = 1;
                _speed = speed > 0 ? speed : DefaultSpeed;
            }
            return Task.FromResult(true);
        }

        public Task<bool> ReverseAsync(double speed)
        {
            lock (_sync)
            {
                if (!_enabled || _alarm) return Task.FromResult(false);
                _direction = -1;
                _speed = speed > 0 ? speed : DefaultSpeed;
            }
            return Task.FromResult(true);
        }

        public Task<bool> StopAsync()
        {
            lock (_sync) { _direction = 0; _speed = 0; }
            return Task.FromResult(true);
        }

        public Task ResetAlarmAsync()
        {
            lock (_sync) _alarm = false;
            return Task.CompletedTask;
        }

        public AxisStatusSnapshot ReadStatus()
        {
            lock (_sync)
            {
                return new AxisStatusSnapshot
                {
                    ComponentId = ComponentId,
                    ActualSpeed = _direction != 0 ? _speed : 0,
                    IsEnabled = _enabled,
                    IsInError = _alarm,
                    IsMoving = _direction != 0,
                    DirectionForward = _direction > 0,
                    DirectionReverse = _direction < 0,
                };
            }
        }
    }
}
