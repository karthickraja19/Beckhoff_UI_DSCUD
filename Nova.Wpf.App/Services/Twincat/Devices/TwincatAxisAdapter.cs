using Nova.Abstractions.Hardware;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Teal.Library.Servo;
using Nova.Alarm;
using Nova.Wpf.App.Services.Twincat.HmiDisplay;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// Nova <see cref="IAxis"/> over one Beckhoff library servo. Commands go through the
    /// library, which writes the request bits. While a screen follows the axis
    /// (<see cref="FollowStatus"/>), the library reads its current status first
    /// (UpdateAxisProperties), then the PLC pushes every change of its HMI display struct -
    /// where the library says it is, read as the struct App.config's ServoHmiDisplayType
    /// names - through <see cref="PlcSubscriptions"/>. The Axis tab shows what the PLC
    /// reports rather than what was last clicked, and reading it never waits on the PLC.
    /// </summary>
    public sealed class TwincatAxisAdapter : IAxis, ILiveStatus, ITwincatStatusSource, ILibraryStatusSource
    {
        private const int PollMs = 50;

        /// <summary>How long a move has to show as running before it is taken as already done.</summary>
        private const int StartWindowMs = 500;

        private const int DefaultMoveTimeoutMs = 60000;

        private readonly AxisServo _servo;
        private readonly TwincatPlcConnection _plc;
        private readonly HmiDisplayStatus _display;

        /// <param name="displayType">The struct App.config's ServoHmiDisplayType names; null when it names none that fits.</param>
        public TwincatAxisAdapter(
            string componentId, AxisServo servo, TwincatPlcConnection plc, PlcSubscriptions subscriptions, Type displayType)
        {
            ComponentId = componentId;
            _servo = servo ?? throw new ArgumentNullException(nameof(servo));
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _display = new HmiDisplayStatus(subscriptions, plc, servo, displayType, servo.UpdateAxisProperties);
        }

        public event EventHandler LibraryStatusRead
        {
            add { _display.LibraryRead += value; }
            remove { _display.LibraryRead -= value; }
        }

        public string ComponentId { get; }

        public IReadOnlyList<string> WatchedSymbols => _display.WatchedSymbols;

        public IDisposable FollowStatus() => _display.Follow();

        public bool IsHomed => ReadStatus().IsHomed;

        public bool SupportsPositioning => true;

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

        /// <summary>
        /// The status the PLC last pushed - or, until it pushes one, what the library read
        /// when the axis was followed. Never touches the PLC, so it is safe on the UI thread.
        /// With neither it says why instead of showing old values as current.
        /// </summary>
        public AxisStatusSnapshot ReadStatus()
        {
            var snapshot = new AxisStatusSnapshot { ComponentId = ComponentId, SpeedLimit = _servo.Velocity };

            IAxisHmiDisplay display;
            string reason;
            if (_display.TryRead(out display, out reason))
                display.Fill(snapshot);
            else if (_display.HasLibraryStatus)
                FillFromLibrary(snapshot);
            else
                snapshot.State = reason;

            return snapshot;
        }

        /// <summary>The servo's properties, as its UpdateAxisProperties last read them.</summary>
        private void FillFromLibrary(AxisStatusSnapshot snapshot)
        {
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
            snapshot.State = _servo.AxisError ? "Error" : (_servo.AxisRunning ? "Running" : "Ready");
        }

        private double SpeedOrDefault(double speed) => speed > 0 ? speed : _servo.Velocity;

        /// <summary>
        /// Sends the move, then watches the pushed status until the axis has run and stopped
        /// again. Only the command takes the PLC lock; the wait reads nothing from the PLC.
        /// The move follows the axis's status itself, so leaving the screen that started it
        /// does not cut the wait short.
        /// </summary>
        private Task<bool> MoveAndWait(Action move)
        {
            return Task.Run(async () =>
            {
                using (FollowStatus())
                {
                    if (!await RunLocked(move).ConfigureAwait(false))
                        return false;

                    int timeoutMs = _servo.Timeout > 0 ? _servo.Timeout : DefaultMoveTimeoutMs;
                    var watch = Stopwatch.StartNew();
                    bool started = false;

                    while (watch.ElapsedMilliseconds < timeoutMs)
                    {
                        Thread.Sleep(PollMs);

                        var status = ReadStatus();
                        if (status.IsInError)
                            return false;

                        if (status.IsMoving)
                            started = true;
                        else if (started || watch.ElapsedMilliseconds >= StartWindowMs)
                            return true;
                    }

                    Debug.WriteLine(ComponentId + " move did not finish within " + timeoutMs + " ms.");
                    return false;
                }
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
