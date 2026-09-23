using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Nova.Abstractions.Hardware;
using Teal.Library.Servo;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// Nova <see cref="IAxis"/> over one Beckhoff library servo. Commands go through the
    /// library, which writes the request bits; status comes from
    /// <see cref="AxisServo.UpdateAxisProperties"/>, so the Axis tab shows what the PLC
    /// reports rather than what was last clicked.
    /// </summary>
    public sealed class TwincatAxisAdapter : IAxis
    {
        private const int PollMs = 50;

        /// <summary>How long a move has to show as running before it is taken as already done.</summary>
        private const int StartWindowMs = 500;

        private const int DefaultMoveTimeoutMs = 60000;

        private readonly AxisServo _servo;
        private readonly TwincatPlcConnection _plc;

        public TwincatAxisAdapter(string componentId, AxisServo servo, TwincatPlcConnection plc)
        {
            ComponentId = componentId;
            _servo = servo ?? throw new ArgumentNullException(nameof(servo));
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
        }

        public string ComponentId { get; }

        /// <summary>As of the last <see cref="ReadStatus"/>.</summary>
        public bool IsHomed => _servo.Homed;

        public Task<bool> EnableAsync() => RunLocked(_servo.On);

        public Task<bool> DisableAsync() => RunLocked(_servo.Off);

        public Task ResetAsync() => RunLocked(_servo.Reset);

        public Task StopAsync() => RunLocked(_servo.Stop);

        /// <summary>
        /// The Axis tab's point-to-point cycle chains moves on these, so each one waits for
        /// the PLC to report the axis stopped. True when it stopped without an axis error.
        /// </summary>
        public Task<bool> MoveAbsAsync(double position, double speed)
            => MoveAndWait(() => _servo.MoveAbsolute(position, SpeedOrDefault(speed)));

        public Task<bool> MoveRelAsync(double distance, double speed)
            => MoveAndWait(() => _servo.MoveRelative(distance, SpeedOrDefault(speed)));

        public Task<bool> HomeAsync(double velocity)
            => MoveAndWait(() => _servo.Home(velocity > 0 ? velocity : _servo.Velocity));

        public Task JogStartAsync(bool forward, double speed)
        {
            double velocity = SpeedOrDefault(speed);
            return forward
                ? RunLocked(() => _servo.JogForward(velocity))
                : RunLocked(() => _servo.JogBackward(velocity));
        }

        /// <summary>The Axis tab does not say which way it jogged, so both jog bits are dropped.</summary>
        public Task JogStopAsync() => RunLocked(() =>
        {
            _servo.JogForwardStop();
            _servo.JogBackwardStop();
        });

        /// <summary>Called by the axis monitor every 250 ms. Never throws.</summary>
        public AxisStatusSnapshot ReadStatus()
        {
            var snapshot = new AxisStatusSnapshot { ComponentId = ComponentId, SpeedLimit = _servo.Velocity };

            try
            {
                lock (_plc.SyncRoot)
                {
                    if (!_plc.IsConnected)
                    {
                        snapshot.State = "PLC not connected";
                        return snapshot;
                    }

                    _servo.UpdateAxisProperties();

                    snapshot.ActualPosition = _servo.CurrentPosition;
                    snapshot.ActualSpeed = Math.Abs(_servo.CurrentVelocity);
                    snapshot.IsEnabled = _servo.Enabled;
                    snapshot.IsInError = _servo.AxisError;
                    snapshot.IsHomed = _servo.Homed;
                    snapshot.IsMoving = _servo.AxisRunning;
                    snapshot.PositiveLimit = _servo.PositiveLimitSense;
                    snapshot.NegativeLimit = _servo.NegativeLimitSense;
                    snapshot.AtHomeSensor = _servo.HomeSense;
                    snapshot.DirectionForward = _servo.CurrentVelocity > 0;
                    snapshot.DirectionReverse = _servo.CurrentVelocity < 0;
                    snapshot.State = snapshot.IsInError ? "Error" : (snapshot.IsMoving ? "Running" : "Ready");
                }
            }
            catch (Exception ex)
            {
                snapshot.State = "Read failed: " + ex.Message;
            }

            return snapshot;
        }

        private double SpeedOrDefault(double speed) => speed > 0 ? speed : _servo.Velocity;

        /// <summary>
        /// Sends the move, then polls until the axis has run and stopped again. The lock is
        /// taken per poll rather than for the whole move, so the monitor and the other
        /// devices keep reading while the axis travels.
        /// </summary>
        private Task<bool> MoveAndWait(Action move)
        {
            return Task.Run(async () =>
            {
                if (!await RunLocked(move).ConfigureAwait(false))
                    return false;

                int timeoutMs = _servo.Timeout > 0 ? _servo.Timeout : DefaultMoveTimeoutMs;
                var watch = Stopwatch.StartNew();
                bool started = false;

                while (watch.ElapsedMilliseconds < timeoutMs)
                {
                    Thread.Sleep(PollMs);

                    bool running, error;
                    lock (_plc.SyncRoot)
                    {
                        _servo.UpdateAxisProperties();
                        running = _servo.AxisRunning;
                        error = _servo.AxisError;
                    }

                    if (error)
                        return false;

                    if (running)
                        started = true;
                    else if (started || watch.ElapsedMilliseconds >= StartWindowMs)
                        return true;
                }

                Debug.WriteLine(ComponentId + " move did not finish within " + timeoutMs + " ms.");
                return false;
            });
        }

        private Task<bool> RunLocked(Action action)
        {
            return Task.Run(() =>
            {
                lock (_plc.SyncRoot)
                {
                    try
                    {
                        if (!_plc.IsConnected)
                            return false;

                        action();
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ComponentId + " command failed: " + ex.Message);
                        return false;
                    }
                }
            });
        }
    }
}
