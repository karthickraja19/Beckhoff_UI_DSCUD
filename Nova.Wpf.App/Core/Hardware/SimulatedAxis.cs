using System;
using System.Threading.Tasks;
using Nova.Abstractions.Hardware;

namespace Nova.Core.Hardware
{
    /// <summary>
    /// Development stand-in for a servo axis. Position advances toward the last
    /// commanded target (or continuously while jogging) at the commanded speed,
    /// so the Manual screen's live values, status chips, and buttons can all be
    /// exercised without a motion card. Soft limits at ±500 mm trip the limit flags.
    /// </summary>
    public sealed class SimulatedAxis : IAxis
    {
        private const double MinPosition = -500;
        private const double MaxPosition = 500;
        private const int MoveTimeoutMs = 60000;

        /// <summary>Stand-in for the Axis_Parameters.xml speed, so the Manual screen's
        /// speed validation is exercised on a dev PC too.</summary>
        private const double ConfiguredSpeedLimit = 100;

        private readonly object _sync = new object();

        private double _position;
        private double _velocity;
        private double _target;
        private double _commandSpeed;
        private bool _moving;
        private int _jogDirection;          // -1, 0, +1
        private double _jogSpeed;
        private bool _enabled;
        private bool _error;
        private bool _homed;
        private DateTime _lastTick = DateTime.UtcNow;

        public string ComponentId { get; }

        public bool IsHomed
        {
            get { lock (_sync) return _homed; }
        }

        public SimulatedAxis(string componentId)
        {
            ComponentId = componentId;
        }

        public Task<bool> EnableAsync()
        {
            lock (_sync) { Advance(); _enabled = true; }
            return Task.FromResult(true);
        }

        public Task<bool> DisableAsync()
        {
            lock (_sync) { Advance(); _enabled = false; _moving = false; _jogDirection = 0; }
            return Task.FromResult(true);
        }

        public Task ResetAsync()
        {
            lock (_sync) _error = false;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            lock (_sync) { Advance(); _moving = false; _jogDirection = 0; _velocity = 0; }
            return Task.CompletedTask;
        }

        public async Task<bool> MoveAbsAsync(double position, double speed)
        {
            if (!BeginMove(position, speed))
                return false;
            return await WaitForMoveDoneAsync();
        }

        public async Task<bool> MoveRelAsync(double distance, double speed)
        {
            double target;
            lock (_sync) { Advance(); target = _position + distance; }
            if (!BeginMove(target, speed))
                return false;
            return await WaitForMoveDoneAsync();
        }

        public async Task<bool> HomeAsync(double velocity)
        {
            if (!BeginMove(0, velocity))
                return false;
            bool done = await WaitForMoveDoneAsync();
            if (done)
                lock (_sync) _homed = true;
            return done;
        }

        public Task JogStartAsync(bool forward, double speed)
        {
            lock (_sync)
            {
                Advance();
                if (_enabled && speed > 0)
                {
                    _moving = false;
                    _jogDirection = forward ? 1 : -1;
                    _jogSpeed = speed;
                }
            }
            return Task.CompletedTask;
        }

        public Task JogStopAsync()
        {
            lock (_sync) { Advance(); _jogDirection = 0; _velocity = 0; }
            return Task.CompletedTask;
        }

        public AxisStatusSnapshot ReadStatus()
        {
            lock (_sync)
            {
                Advance();
                return new AxisStatusSnapshot
                {
                    ComponentId = ComponentId,
                    ActualPosition = _position,
                    ActualSpeed = Math.Abs(_velocity),
                    SpeedLimit = ConfiguredSpeedLimit,
                    IsEnabled = _enabled,
                    IsInError = _error,
                    NegativeLimit = _position <= MinPosition,
                    PositiveLimit = _position >= MaxPosition,
                    AtHomeSensor = _homed && Math.Abs(_position) < 0.01,
                    IsHomed = _homed,
                    IsMoving = _moving || _jogDirection != 0,
                    DirectionForward = _velocity > 0,
                    DirectionReverse = _velocity < 0,
                    State = DescribeState(),
                };
            }
        }

        /// <summary>
        /// Advantech's own state names, so the Manual screen's state readout shows the
        /// same vocabulary on a dev PC as it does against a real drive. Caller holds
        /// <see cref="_sync"/>.
        /// </summary>
        private string DescribeState()
        {
            if (_error) return "STA_AX_ERROR_STOP";
            if (!_enabled) return "STA_AX_DISABLE";
            if (_jogDirection != 0) return "STA_AX_EXT_JOG";
            if (_moving) return "STA_AX_PTP_MOT";
            return "STA_AX_READY";
        }

        private bool BeginMove(double target, double speed)
        {
            lock (_sync)
            {
                Advance();
                if (!_enabled || speed <= 0)
                    return false;
                _jogDirection = 0;
                _target = Clamp(target);
                _commandSpeed = speed;
                _moving = true;
                return true;
            }
        }

        private async Task<bool> WaitForMoveDoneAsync()
        {
            var start = DateTime.UtcNow;
            while ((DateTime.UtcNow - start).TotalMilliseconds < MoveTimeoutMs)
            {
                await Task.Delay(50);
                lock (_sync)
                {
                    Advance();
                    if (!_moving)
                        return _enabled;
                }
            }
            return false;
        }

        /// <summary>Integrates position forward from the last observation. Callers hold _sync.</summary>
        private void Advance()
        {
            var now = DateTime.UtcNow;
            double dt = (now - _lastTick).TotalSeconds;
            _lastTick = now;
            if (dt <= 0)
                return;

            if (!_enabled)
            {
                _velocity = 0;
                _moving = false;
                _jogDirection = 0;
                return;
            }

            if (_jogDirection != 0)
            {
                _position = Clamp(_position + _jogDirection * _jogSpeed * dt);
                _velocity = _jogDirection * _jogSpeed;
                if (_position <= MinPosition || _position >= MaxPosition)
                {
                    _jogDirection = 0;
                    _velocity = 0;
                }
            }
            else if (_moving)
            {
                double step = _commandSpeed * dt;
                double diff = _target - _position;
                if (Math.Abs(diff) <= step)
                {
                    _position = _target;
                    _moving = false;
                    _velocity = 0;
                }
                else
                {
                    _position += Math.Sign(diff) * step;
                    _velocity = Math.Sign(diff) * _commandSpeed;
                }
            }
            else
            {
                _velocity = 0;
            }
        }

        private static double Clamp(double value)
            => value < MinPosition ? MinPosition : (value > MaxPosition ? MaxPosition : value);
    }
}
