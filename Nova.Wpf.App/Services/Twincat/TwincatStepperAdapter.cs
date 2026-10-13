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
    /// tab. The library gives an I/O stepper Forward, Reverse, Stop and Reset; it runs at its
    /// own speed and has no on or off, so the tab's speed is ignored and enabling it does nothing.
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

        /// <summary>Runs forward at the stepper's own speed; <paramref name="speed"/> is ignored.</summary>
        public Task<bool> ForwardAsync(double speed) => RunLocked(_stepper.Forward);

        /// <summary>Runs in reverse at the stepper's own speed; <paramref name="speed"/> is ignored.</summary>
        public Task<bool> ReverseAsync(double speed) => RunLocked(_stepper.Reverse);

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
