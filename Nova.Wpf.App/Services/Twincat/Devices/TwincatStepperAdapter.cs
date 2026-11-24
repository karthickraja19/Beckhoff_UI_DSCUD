using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nova.Abstractions.Hardware;
using Nova.Alarm;
using Nova.Wpf.App.Services.Twincat.HmiDisplay;
using Teal.Library.Stepper;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// Nova <see cref="IStepper"/> over one Beckhoff library I/O stepper, driven from the Stepper
    /// tab. The library gives an I/O stepper Forward, Reverse, Stop and Reset, and its manual
    /// velocity (stepperdata[Id].par.fMvelocity, % of the EL7047's speed range) - the tab's
    /// velocity box. It has no on or off, so enabling it does nothing.
    /// </summary>
    /// <remarks>
    /// Status - running and error - is read as an axis's is: while the Stepper tab follows the
    /// stepper (<see cref="FollowStatus"/>), the library reads its current status first
    /// (UpdateIOStepperProperties), then the PLC pushes every change of its HMI display struct
    /// - where the library says it is, read as the struct App.config's IOStepperHmiDisplayType
    /// names - through <see cref="PlcSubscriptions"/>.
    /// </remarks>
    public sealed class TwincatStepperAdapter : IStepper, ILiveStatus, ITwincatStatusSource, ILibraryStatusSource
    {
        private readonly IOStepper _stepper;
        private readonly TwincatPlcConnection _plc;
        private readonly HmiDisplayStatus _display;

        /// <param name="displayType">The struct App.config's IOStepperHmiDisplayType names; null when it names none that fits.</param>
        public TwincatStepperAdapter(
            string componentId, IOStepper stepper, TwincatPlcConnection plc, PlcSubscriptions subscriptions, Type displayType)
        {
            ComponentId = componentId;
            _stepper = stepper ?? throw new ArgumentNullException(nameof(stepper));
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _display = new HmiDisplayStatus(subscriptions, plc, stepper, displayType, stepper.UpdateIOStepperProperties);
        }

        public event EventHandler LibraryStatusRead
        {
            add { _display.LibraryRead += value; }
            remove { _display.LibraryRead -= value; }
        }

        public IDisposable FollowStatus() => _display.Follow();

        public string ComponentId { get; }

        public IReadOnlyList<string> WatchedSymbols => _display.WatchedSymbols;

        public bool IsAxisBased => false;

        /// <summary>An I/O stepper has no on or off; the Stepper tab calls this before each move.</summary>
        public Task<bool> EnableAsync() => Task.FromResult(true);

        /// <summary>The PLC's manual velocity is % of the EL7047's speed range.</summary>
        public double MaxSpeed => IOStepper.MaxVelocity;

        /// <summary>Reads the PLC's manual velocity (par.fMvelocity) once, through the library.</summary>
        public Task<double?> ReadSpeedAsync()
        {
            return Task.Run(() =>
            {
                lock (_plc.SyncRoot)
                {
                    try
                    {
                        if (!_plc.IsConnected)
                            return (double?)null;

                        _stepper.UpdateIOStepperProperties();
                        double velocity = _stepper.ManualVelocity;
                        return double.IsNaN(velocity) ? (double?)null : velocity;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(ComponentId + " velocity read failed: " + ex.Message);
                        return null;
                    }
                }
            });
        }

        /// <summary>
        /// Runs forward; a <paramref name="speed"/> &gt; 0 is first written as the PLC's manual
        /// velocity (% of the drive's range), which the PLC jogs with in MANUAL.
        /// </summary>
        public Task<bool> ForwardAsync(double speed) => RunWithSpeed(speed, _stepper.Forward);

        /// <summary>Runs in reverse; <paramref name="speed"/> as for <see cref="ForwardAsync"/>.</summary>
        public Task<bool> ReverseAsync(double speed) => RunWithSpeed(speed, _stepper.Reverse);

        public Task<bool> StopAsync() => RunLocked(_stepper.Stop);

        public Task ResetAlarmAsync() => RunLocked(_stepper.Reset);

        /// <summary>Stop sent from the shutdown path, so a manual move started here does not outlive the HMI.</summary>
        public void StopOnExit()
        {
            try
            {
                lock (_plc.SyncRoot)
                {
                    if (_plc.IsConnected)
                        _stepper.Stop();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ComponentId + " stop on exit failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Running and error as the PLC last pushed them - or, until it pushes them, as the
        /// library read them when the stepper was followed. Never touches the PLC, so it is
        /// safe on any thread; with neither it says why instead of showing old values as current.
        /// </summary>
        public AxisStatusSnapshot ReadStatus()
        {
            var snapshot = new AxisStatusSnapshot { ComponentId = ComponentId };

            IAxisHmiDisplay display;
            string reason;
            if (_display.TryRead(out display, out reason))
            {
                display.Fill(snapshot);
            }
            else if (_display.HasLibraryStatus)
            {
                snapshot.IsInError = _stepper.Error;
                snapshot.IsMoving = _stepper.Running;
                snapshot.State = _stepper.Error ? "Error" : (_stepper.Running ? "Running" : "Ready");
            }
            else
            {
                snapshot.State = reason;
            }

            return snapshot;
        }

        /// <summary>Sets the velocity (when one is given) and starts the run, under one PLC lock; a velocity not taken does not start it.</summary>
        private Task<bool> RunWithSpeed(double speed, Action run)
        {
            return Task.Run(() =>
            {
                lock (_plc.SyncRoot)
                {
                    try
                    {
                        if (!_plc.IsConnected)
                            return false;

                        if (speed > 0 && !_stepper.SetManualVelocity(speed))
                            return false;

                        run();
                        return true;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(ComponentId + " command failed: " + ex.Message);
                        return false;
                    }
                }
            });
        }

        /// <summary>The library's commands are void and log their own failures, so a command that did not take shows in the log.</summary>
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
                        System.Diagnostics.Debug.WriteLine(ComponentId + " command failed: " + ex.Message);
                        return false;
                    }
                }
            });
        }
    }
}
