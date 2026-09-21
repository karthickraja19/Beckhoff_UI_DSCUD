using System;
using System.Threading.Tasks;
using Nova.Abstractions.Hardware;
using Teal.Library.Stepper;
using TealStepper = Teal.Library.Contracts.IStepper;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// Nova <see cref="IStepper"/> over one Beckhoff library stepper bound to its ST_STEPPER
    /// in the PLC (GLOBAL_STEPPER.gStepper[Id]). Commands set hmicmd bits through the
    /// library, which FB_STEPPER latches into cmd and clears itself; status is read back
    /// from the same ST_STEPPER, so the Stepper tab shows what the PLC reports rather than
    /// what was last clicked.
    /// </summary>
    public sealed class TwincatStepperAdapter : IStepper
    {
        private readonly TealStepper _stepper;
        private readonly TwincatPlcConnection _plc;

        public TwincatStepperAdapter(string componentId, string instance, TealStepper stepper, TwincatPlcConnection plc)
        {
            ComponentId = componentId;
            Instance = instance;
            _stepper = stepper ?? throw new ArgumentNullException(nameof(stepper));
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
        }

        public string ComponentId { get; }

        /// <summary>PLC symbol of this stepper, e.g. GLOBAL_STEPPER.gStepper[1].</summary>
        public string Instance { get; }

        public bool IsAxisBased => _stepper.IsAxisBased;

        /// <summary>
        /// FB_STEPPER only runs while in.blockEnable and in.operationEnable are set; On()
        /// sets both and selects the mode. It is idempotent, so the Stepper tab enabling
        /// before every move is safe.
        /// </summary>
        public Task<bool> EnableAsync() => RunLocked(() => _stepper.On());

        /// <summary>A speed of -1 uses the Speed configured in Data\Steppers.json.</summary>
        public Task<bool> ForwardAsync(double speed) => RunLocked(() => _stepper.Forward(speed));

        public Task<bool> ReverseAsync(double speed) => RunLocked(() => _stepper.Reverse(speed));

        public Task<bool> StopAsync() => RunLocked(() => _stepper.Stop());

        public Task ResetAlarmAsync() => RunLocked(() => _stepper.Reset());

        /// <summary>Stop sent from the shutdown path, so a manual move started here does not outlive the HMI.</summary>
        public void StopOnExit()
        {
            try
            {
                lock (_plc.SyncRoot)
                {
                    _stepper.Stop();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ComponentId + " stop on exit failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Called by the axis monitor every 250 ms. Never throws: a failed read shows in
        /// <see cref="AxisStatusSnapshot.State"/> instead.
        /// </summary>
        public AxisStatusSnapshot ReadStatus()
        {
            var snapshot = new AxisStatusSnapshot { ComponentId = ComponentId };

            try
            {
                lock (_plc.SyncRoot)
                {
                    if (!_plc.IsConnected)
                    {
                        snapshot.State = "PLC not connected";
                        return snapshot;
                    }

                    snapshot.IsEnabled = _stepper.PowerOn;
                    snapshot.IsInError = _stepper.AlarmHappen() || _stepper.HasError;
                    snapshot.IsMoving = _stepper.IsRunning;
                    snapshot.DirectionForward = _stepper.IsForward;
                    snapshot.DirectionReverse = _stepper.IsReverse;

                    if (IsAxisBased)
                    {
                        snapshot.ActualPosition = _stepper.ActualPosition;
                        snapshot.ActualSpeed = _stepper.ActualVelocity;
                        snapshot.IsHomed = _stepper.IsHomed;
                        snapshot.PositiveLimit = _stepper.PositiveLimit;
                        snapshot.NegativeLimit = _stepper.NegativeLimit;
                    }
                    else
                    {
                        // An I/O conveyor has no encoder. io.lVelocity is the setpoint the PLC
                        // is sending the drive (negative in reverse), the closest thing to a
                        // speed readout it has.
                        var ioStepper = _stepper as IOStepper;
                        snapshot.ActualSpeed = ioStepper != null ? Math.Abs(ioStepper.CommandedVelocity) : 0;
                    }

                    snapshot.State = snapshot.IsInError
                        ? "Error " + _stepper.ErrorId
                        : (snapshot.IsMoving ? "Running" : "Ready");
                }
            }
            catch (Exception ex)
            {
                snapshot.State = "Read failed: " + ex.Message;
            }

            return snapshot;
        }

        private Task<bool> RunLocked(Func<bool> action)
        {
            return Task.Run(() =>
            {
                lock (_plc.SyncRoot)
                {
                    return action();
                }
            });
        }
    }
}
