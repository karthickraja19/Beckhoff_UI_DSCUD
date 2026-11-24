using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Master.Sequences.FiveAxis;
using Nova.Abstractions.Hardware;
using Nova.Abstractions.Services;

namespace Master.Sequences
{
    /// <summary>
    /// The 5 axis (kinematic) calibration, run by the HMI. The UI's Start button calls <see cref="Start"/>,
    /// its Stop button calls <see cref="Stop"/>.
    ///
    /// This file only starts, stops and reports. The sequence itself (the SOP's steps 1 to 4) is
    /// FiveAxisCalibration.Sequence.cs; the maths is in the FiveAxis folder.
    /// </summary>
    public sealed partial class FiveAxisCalibration
    {
        private readonly IAxisService _axes;
        private readonly ICncAxisProvider _cncAxes;
        private readonly IFiveAxisMotion _motion;
        private readonly object _gate = new object();

        private CancellationTokenSource _cts;
        private Task _run = Task.CompletedTask;
        private int _station = 1;

        public FiveAxisCalibration(IAxisService axes, ICncAxisProvider cncAxes, IFiveAxisMotion motion)
        {
            _axes = axes;
            _cncAxes = cncAxes;
            _motion = motion;
            VisionFactory = settings => new C5AVisionClient(settings);
        }

        /// <summary>Plate pitch, image size, vision address and so on; the service sets it from Data\Calibration\FiveAxisSettings.json before each Start.</summary>
        public FiveAxisSettings Settings { get; set; } = new FiveAxisSettings();

        /// <summary>Makes the vision connection of a run. Replaced only by a test.</summary>
        public Func<FiveAxisSettings, IFiveAxisVision> VisionFactory { get; set; }

        public bool IsRunning { get; private set; }

        /// <summary>"Running", "Completed.", "Stopped." or the error; empty until the first run and after <see cref="Reset"/>.</summary>
        public string Message { get; private set; } = string.Empty;

        public bool IsDone { get; private set; }

        public bool IsError { get; private set; }

        /// <summary>What the last completed run found; null until one completes and after <see cref="Reset"/>.</summary>
        public FiveAxisResult Result { get; private set; }

        /// <summary>Raised on any thread when <see cref="IsRunning"/> or <see cref="Message"/> changes.</summary>
        public event EventHandler Changed;

        /// <summary>Raised on the sequence's thread when a run has its result, before it is reported as done (the service saves it).</summary>
        public event EventHandler<FiveAxisResult> ResultReady;

        /// <summary>Start button: runs the sequence on the station's tables.</summary>
        public void Start(FiveAxisTeach teach)
        {
            if (teach == null)
                throw new ArgumentNullException(nameof(teach));

            string problem = Settings.Problem();
            if (problem.Length > 0)
                throw new InvalidOperationException(problem);

            if (_motion == null || !_motion.IsAvailable)
                throw new InvalidOperationException(_motion == null ? "No five axis motion is wired." : _motion.UnavailableReason);

            CancellationToken token;
            lock (_gate)
            {
                if (IsRunning)
                    throw new InvalidOperationException("The 5 axis calibration is already running.");

                _cts = new CancellationTokenSource();
                token = _cts.Token;
                _station = teach.Station;
                IsRunning = true;
                IsDone = false;
                IsError = false;
                Result = null;
                Message = "Running";
            }

            Changed?.Invoke(this, EventArgs.Empty);
            FiveAxisSettings settings = Settings;
            _run = Task.Run(() => Run(teach, settings, token));
        }

        /// <summary>Stop button: stops the channel and every axis and ends the sequence.</summary>
        public void Stop()
        {
            lock (_gate)
            {
                if (!IsRunning)
                    return;

                _cts.Cancel();
            }

            StopAxes(reset: true);
        }

        /// <summary>Clears the last result.</summary>
        public void Reset()
        {
            lock (_gate)
            {
                if (IsRunning)
                    return;

                IsDone = false;
                IsError = false;
                Message = string.Empty;
                Result = null;
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Stops the sequence and waits for it, when the application closes.</summary>
        public void StopAndWait()
        {
            Stop();
            _run.Wait(TimeSpan.FromSeconds(5));
        }

        private async Task Run(FiveAxisTeach teach, FiveAxisSettings settings, CancellationToken token)
        {
            string message = "Completed.";
            bool error = false;
            FiveAxisResult result = null;
            try
            {
                result = await Sequence(teach, settings, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                message = "Completed. E max " + result.ErrorMax.ToString("0.###") + " mm, average " + result.ErrorAverage.ToString("0.###") + " mm.";
            }
            catch (Exception ex)
            {
                // The Stop button has stopped and reset the channel; a fault only stops it, so its error stays visible.
                StopAxes(reset: false);
                if (token.IsCancellationRequested)
                {
                    message = "Stopped.";
                }
                else
                {
                    message = ex.Message;
                    error = true;
                    FiveAxisLog.Error("Sequence", "The 5 axis calibration failed: " + ex.Message, ex.ToString());
                }
                result = null;
            }

            if (result != null)
            {
                try
                {
                    ResultReady?.Invoke(this, result);
                }
                catch (Exception ex)
                {
                    message = "Completed, but the result could not be saved: " + ex.Message;
                    FiveAxisLog.Error("Sequence", message, ex.ToString());
                }
            }

            lock (_gate)
            {
                IsRunning = false;
                IsError = error;
                IsDone = !error && !token.IsCancellationRequested;
                Message = message;
                Result = result;
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Shows what the sequence is doing (the Calibration tab's status line).</summary>
        private void Report(string text)
        {
            lock (_gate)
            {
                if (!IsRunning)
                    return;

                Message = text;
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>The servo or CNC axis with this id, for the sequence to move. Throws when there is none.</summary>
        private IAxis Axis(string componentId)
        {
            IAxis axis = _axes?.GetAxis(componentId)
                ?? _cncAxes?.Axes.FirstOrDefault(a => string.Equals(a.ComponentId, componentId, StringComparison.OrdinalIgnoreCase));

            if (axis == null)
                throw new InvalidOperationException("The machine has no axis '" + componentId + "'.");

            return axis;
        }

        private void StopAxes(bool reset)
        {
            var all = new List<IAxis>();
            if (_axes != null)
                all.AddRange(_axes.GetAllAxes());
            if (_cncAxes != null)
                all.AddRange(_cncAxes.Axes);

            var stops = all.Select(a => Task.Run(() => a.StopAsync())).ToList();
            if (_motion != null)
            {
                int station = _station;
                stops.Add(Task.Run(() => _motion.StopAsync(station, reset)));
            }

            try
            {
                Task.WaitAll(stops.ToArray(), TimeSpan.FromSeconds(3));
            }
            catch (Exception)
            {
                // An axis that cannot be stopped must not stop the others from being stopped.
            }
        }
    }
}

