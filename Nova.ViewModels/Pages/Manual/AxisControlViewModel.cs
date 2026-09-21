using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Data.Models;
using Nova.Abstractions.Hardware;
using Nova.Abstractions.Hardware.Enums;
using Nova.Abstractions.Services;
using Nova.ViewModels.Common;

namespace Nova.ViewModels.Pages.Manual
{
   
    /// <summary>
    /// ViewModel backing the "Axis" tab of the Manual screen. Operator inputs
    /// (targets, speeds) live here as strings; commands delegate to the selected
    /// <see cref="IAxis"/> via <see cref="IAxisService"/>, and live read-back
    /// arrives through <see cref="IAxisMonitorService"/> events.
    /// </summary>
    public partial class AxisControlViewModel : ViewModelBase
    {
        private readonly IAxisService _axisService;
        private readonly IAxisMonitorService _monitor;
        private readonly IDialogService _dialog;
        private readonly IMachineSettingsService _settings;

        /// <summary>Message V3's ManualScreen shows for every out-of-range manual speed
        /// (home, jog, move, point-to-point). Kept verbatim so operators and the QA
        /// scripts see the same wording they already know.</summary>
        private const string SpeedRangeMessage =
            "Please Enter Speed greater than 0 and less than Axis Parameter speed";

        private const string DialogTitle = "Manual Screen";

        /// <summary>
        /// Ceiling for the point-to-point iteration count, and the digit limit of the
        /// box it is typed into — V3's App.config PtpIterationCount and PPCIterationLen,
        /// which its ManualScreen reads through ConfigurationManager (MovePTP and
        /// txtIteration_KeyPress respectively).
        ///
        /// Nova.ViewModels targets netstandard2.0 and so cannot read App.config itself;
        /// the WPF host assigns these once at startup from the same keys. The defaults
        /// are the values V3 ships, so a host that never sets them — the designer, a
        /// test — still behaves exactly as V3 does.
        /// </summary>
        public static int MaxIterations { get; set; } = 100;

        public static int MaxIterationDigits { get; set; } = 3;

        /// <summary>V3's MovePTP wording for a rejected iteration count, kept verbatim so
        /// operators and the QA scripts see what they already know.</summary>
        private static string IterationRangeMessage =>
            "Please enter Iteration count greater than 0 Less than " +
            MaxIterations.ToString(CultureInfo.InvariantCulture);

        private CancellationTokenSource _ptpCts;
        private bool _syncingFromHardware;

        // ---- Axis selection ------------------------------------------------
        public ObservableCollection<string> Axes { get; }

        [ObservableProperty]
        private string _selectedAxis;

        // ---- Axis Device Control ------------------------------------------
        [ObservableProperty]
        private bool _isEnabled;

        // ---- Motion Control -----------------------------------------------
        [ObservableProperty]
        private string _position;

        [ObservableProperty]
        private string _speed;

        // ---- Homing Control -----------------------------------------------
        [ObservableProperty]
        private string _homeVelocity;

        // ---- Point to point -----------------------------------------------
        [ObservableProperty]
        private string _startPosition;

        [ObservableProperty]
        private string _endPosition;

        [ObservableProperty]
        private string _pointToPointSpeed;

        [ObservableProperty]
        private string _wait;

        [ObservableProperty]
        private string _iterations;

        // ---- Jog Control --------------------------------------------------
        [ObservableProperty]
        private string _jogSpeed;

        // ---- Continuous Motion --------------------------------------------
        [ObservableProperty]
        private string _continuousSpeed;

        // ---- Live read-back values ----------------------------------------
        [ObservableProperty]
        private double _actualPosition;

        [ObservableProperty]
        private double _actualSpeed;

        /// <summary>
        /// The drive's own state word (AdvantechAxis.GetAxisStatus(), e.g.
        /// "STA_AX_READY" / "STA_AX_ERROR_STOP"), shown under Clear error. The status
        /// chips answer "is it enabled / moving / faulted"; when an axis refuses a
        /// command for a reason none of those flags covers, this is the only thing on
        /// screen that says why, and reading it off the drive beats walking an engineer
        /// through Advantech's utility. Shown verbatim so it matches the vendor
        /// documentation an engineer would look it up in.
        /// </summary>
        [ObservableProperty]
        private string _axisState = UnknownState;

        /// <summary>Shown before the first poll, and for a device that reports no state
        /// word at all (an IO stepper has none).</summary>
        private const string UnknownState = "—";

        /// <summary>
        /// Axis parameter speed for the selected axis, read back from the monitor.
        /// Zero until the first poll arrives, or when the axis reports no configured
        /// limit — treated as "unlimited" so a machine with an unset parameter is not
        /// left unable to move.
        /// </summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(VelocityHint))]
        [NotifyPropertyChangedFor(nameof(HomeVelocityHint))]
        private double _speedLimit;

        /// <summary>Placeholder for the velocity boxes, showing the ceiling so the operator
        /// knows the range before typing rather than after being refused. The screen says
        /// "Velocity" throughout — the backing members keep their original Speed names
        /// because they mirror the Teal/Advantech API and V3 field names.</summary>
        public string VelocityHint => SpeedLimit > 0
            ? "Velocity (mm/s), max " + SpeedLimit.ToString("0.##", CultureInfo.InvariantCulture)
            : "Velocity (mm/s)";

        public string HomeVelocityHint => SpeedLimit > 0
            ? "Home velocity (mm/s), max " + SpeedLimit.ToString("0.##", CultureInfo.InvariantCulture)
            : "Home velocity (mm/s)";

        /// <summary>Placeholder for the iteration box. The velocity boxes already show
        /// their ceiling; the iteration box had one too and never said so, which reads
        /// as an arbitrary refusal when a large count is rejected.</summary>
        public string IterationHint =>
            "Iteration count, max " + MaxIterations.ToString(CultureInfo.InvariantCulture);

        /// <summary>Digit limit for the iteration box (V3's PPCIterationLen), bound to
        /// TextBox.MaxLength so an over-long count cannot be typed in the first place.</summary>
        public int IterationDigits => MaxIterationDigits;

        // ---- Device status (data-driven indicator list) -------------------
        public ObservableCollection<AxisStatusItem> DeviceStatus { get; }

        private readonly AxisStatusItem _enableStatus;
        private readonly AxisStatusItem _errorStatus;
        private readonly AxisStatusItem _negativeLimitStatus;
        private readonly AxisStatusItem _positiveLimitStatus;
        private readonly AxisStatusItem _homeSensorStatus;
        private readonly AxisStatusItem _homedStatus;
        private readonly AxisStatusItem _runningStatus;

        public AxisControlViewModel(IAxisService axisService, IAxisMonitorService monitor,
            IDialogService dialog = null, IMachineSettingsService settings = null)
        {
            _axisService = axisService;
            _monitor = monitor;
            _dialog = dialog;
            _settings = settings;

            Axes = new ObservableCollection<string>();
            var all = _axisService?.GetAllAxes();
            if (all != null && all.Count > 0)
            {
                foreach (var axis in all)
                    Axes.Add(axis.ComponentId);
            }
            else
            {
                foreach (var name in Enum.GetNames(typeof(Servos)))
                    Axes.Add(name);
            }

            _selectedAxis = Axes.Count > 0 ? Axes[0] : null;

            _enableStatus        = new AxisStatusItem("Axis enable");
            _errorStatus         = new AxisStatusItem("Axis in error", isAlarm: true);
            _negativeLimitStatus = new AxisStatusItem("Negative limit", isAlarm: true);
            _homeSensorStatus    = new AxisStatusItem("Axis home");
            _homedStatus         = new AxisStatusItem("Axis homed");
            _runningStatus       = new AxisStatusItem("Axis running");
            _positiveLimitStatus = new AxisStatusItem("Positive limit", isAlarm: true);

            // Display order signed off by QA: Enable, Running, Error, Homed, Positive,
            // Origin (home sensor), Negative. The collection order is the display order —
            // the view binds this straight to a WrapPanel — so it is set here rather than
            // in XAML.
            DeviceStatus = new ObservableCollection<AxisStatusItem>
            {
                _enableStatus,
                _runningStatus,
                _errorStatus,
                _homedStatus,
                _positiveLimitStatus,
                _homeSensorStatus,
                _negativeLimitStatus,
            };

            if (_monitor != null)
                _monitor.AxisStatusUpdated += OnAxisStatusUpdated;

            RefreshAxisParameters();
        }

        // ---- Axis parameters (informational) -------------------------------

        /// <summary>
        /// Configured parameters of the selected axis, shown read-only beside the
        /// controls. Operators had to leave Manual for Settings > Axis Parameters to see
        /// what pitch or homing speed an axis was set up with. Read from the same
        /// Axis_Parameters.xml the Settings table edits, so there is one source of truth.
        /// </summary>
        public ObservableCollection<AxisParameterDetail> AxisParameters { get; } =
            new ObservableCollection<AxisParameterDetail>();

        /// <summary>True when the selected axis has a row in Axis_Parameters.xml.</summary>
        [ObservableProperty]
        private bool _hasAxisParameters;

        partial void OnSelectedAxisChanged(string value)
        {
            RefreshAxisParameters();
        }

        private void RefreshAxisParameters()
        {
            AxisParameters.Clear();

            // Optional dependency: the designer and the simulated stack run without a
            // settings service, and a missing profile must not break the Manual screen.
            if (_settings == null || string.IsNullOrWhiteSpace(SelectedAxis))
            {
                HasAxisParameters = false;
                return;
            }

            try
            {
                var rows = _settings.LoadAxisParameters();
                if (rows == null)
                {
                    HasAxisParameters = false;
                    return;
                }

                AxisParameterRecord match = null;
                foreach (var row in rows)
                {
                    if (string.Equals(row.AxisName, SelectedAxis, StringComparison.OrdinalIgnoreCase))
                    {
                        match = row;
                        break;
                    }
                }

                if (match == null)
                {
                    HasAxisParameters = false;
                    return;
                }

                AxisParameters.Add(new AxisParameterDetail("Card / Axis", match.CardId + " / " + match.AxisId));
                AxisParameters.Add(new AxisParameterDetail("Pitch (mm)", match.Pitch));
                AxisParameters.Add(new AxisParameterDetail("Pulses / mm", match.Pulses));
                AxisParameters.Add(new AxisParameterDetail("PPR", match.Ppr));
                AxisParameters.Add(new AxisParameterDetail("Homing velocity", match.HomingSpeed));
                AxisParameters.Add(new AxisParameterDetail("Homing acc", match.HomingAcc));
                AxisParameters.Add(new AxisParameterDetail("Homing de-acc", match.HomingDeAcc));
                HasAxisParameters = true;
            }
            catch (Exception ex)
            {
                // Informational panel only — never take the Manual screen down for it.
                System.Diagnostics.Debug.WriteLine("Axis parameter load failed: " + ex.Message);
                HasAxisParameters = false;
            }
        }

        private IAxis CurrentAxis => _axisService?.GetAxis(SelectedAxis);

        // Raised on the monitor's background thread: scalar INPC setters only here
        // (WPF marshals those); the DeviceStatus collection itself is never mutated.
        private void OnAxisStatusUpdated(object sender, AxisStatusSnapshot s)
        {
            if (s.ComponentId != SelectedAxis)
                return;

            ActualPosition = s.ActualPosition;
            ActualSpeed = s.ActualSpeed;
            SpeedLimit = s.SpeedLimit;
            AxisState = string.IsNullOrWhiteSpace(s.State) ? UnknownState : s.State.Trim();

            _syncingFromHardware = true;
            IsEnabled = s.IsEnabled;
            _syncingFromHardware = false;

            _enableStatus.IsActive        = s.IsEnabled;
            _errorStatus.IsActive         = s.IsInError;
            _negativeLimitStatus.IsActive = s.NegativeLimit;
            _positiveLimitStatus.IsActive = s.PositiveLimit;
            _homeSensorStatus.IsActive    = s.AtHomeSensor;
            _homedStatus.IsActive         = s.IsHomed;
            _runningStatus.IsActive       = s.IsMoving;
        }

        /// <summary>Enable toggle: commands the drive unless the change came from read-back.</summary>
        partial void OnIsEnabledChanged(bool value)
        {
            _enableStatus.IsActive = value;
            if (_syncingFromHardware)
                return;

            var axis = CurrentAxis;
            if (axis == null)
                return;

            _ = value ? axis.EnableAsync() : axis.DisableAsync();
        }

        // ---- Commands -----------------------------------------------------

        [RelayCommand]
        private async Task ClearError()
        {
            var axis = CurrentAxis;
            if (axis == null) return;
            await axis.ResetAsync();
            _errorStatus.IsActive = false;
        }

        [RelayCommand]
        private async Task Move()
        {
            var axis = CurrentAxis;
            if (axis == null) return;
            if (!TryReadPosition(Position, out double target)) return;
            if (!TryReadSpeed(Speed, out double speed)) return;
            await axis.MoveAbsAsync(target, speed);
        }

        [RelayCommand]
        private async Task Stop()
        {
            CancelPointToPoint();
            var axis = CurrentAxis;
            if (axis == null) return;
            await axis.StopAsync();
        }

        [RelayCommand]
        private async Task Home()
        {
            var axis = CurrentAxis;
            if (axis == null) return;

            // V3's btnHome_Click asks for the velocity before anything else, then gates
            // it on 0 < v <= axis parameter speed. Nova had neither check: a home
            // velocity of 1000 was accepted, TryParse succeeded, and the drive homed at
            // its own configured speed — so the entered value looked simply ignored.
            if (string.IsNullOrWhiteSpace(HomeVelocity))
            {
                ShowMessage("Please Enter the Velocity for Homing");
                return;
            }

            if (!TryReadSpeed(HomeVelocity, out double velocity)) return;
            await axis.HomeAsync(velocity);
        }

        [RelayCommand]
        private async Task HomeStop()
        {
            var axis = CurrentAxis;
            if (axis == null) return;
            await axis.StopAsync();
        }

        
[RelayCommand]
private async Task PointToPointMove()
        {
            // Start and end positions must be different.
            // Compare the actual numeric values rather than the raw strings,
            // so values such as "10" and "10.0" are also treated as equal.
            if (!TryReadPosition(StartPosition, out double start))
                return;

            if (!TryReadPosition(EndPosition, out double end))
                return;

            if (start == end)
            {
                ShowMessage("Enter different values in start and end position fields");
                return;
            }

            var axis = CurrentAxis;
            if (axis == null)
                return;

            if (!TryReadSpeed(PointToPointSpeed, out double speed))
                return;

            if (!TryReadIterations(Iterations, out int iterations))
                return;

            int.TryParse(
                Wait,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int waitMs);

            CancelPointToPoint();
            _ptpCts = new CancellationTokenSource();
            var token = _ptpCts.Token;

            try
            {
                for (int i = 0; i < iterations && !token.IsCancellationRequested; i++)
                {
                    if (!await axis.MoveAbsAsync(end, speed))
                        break;

                    if (waitMs > 0)
                        await Task.Delay(waitMs, token);

                    if (token.IsCancellationRequested)
                        break;

                    if (!await axis.MoveAbsAsync(start, speed))
                        break;

                    if (waitMs > 0)
                        await Task.Delay(waitMs, token);
                }
            }
            catch (OperationCanceledException)
            {
                // Operator pressed Stop mid-cycle.
            }
        }


        [RelayCommand]
        private async Task PointToPointStop()
        {
            CancelPointToPoint();
            var axis = CurrentAxis;
            if (axis == null) return;
            await axis.StopAsync();
        }

        // Jog: started on button press, stopped on release (wired by the view's
        // PreviewMouseDown/Up handlers, mirroring the V3 MouseDown/MouseUp pattern).
        [RelayCommand]
        private async Task JogForwardStart() => await StartJog(true, JogSpeed);

        [RelayCommand]
        private async Task JogReverseStart() => await StartJog(false, JogSpeed);

        [RelayCommand]
        private async Task JogStop()
        {
            var axis = CurrentAxis;
            if (axis == null) return;
            await axis.JogStopAsync();
        }

        // Continuous motion: runs until Stop (Motion Control card) is pressed.
        [RelayCommand]
        private async Task ContinuousForward() => await StartJog(true, ContinuousSpeed);

        [RelayCommand]
        private async Task ContinuousReverse() => await StartJog(false, ContinuousSpeed);

        [RelayCommand]
        private async Task ContinuousStop()
        {
            var axis = CurrentAxis;
            if (axis == null) return;
            await axis.JogStopAsync();
        }

        private async Task StartJog(bool forward, string speedText)
        {
            var axis = CurrentAxis;
            if (axis == null) return;
            if (!TryReadSpeed(speedText, out double speed)) return;
            await axis.JogStartAsync(forward, speed);
        }

        /// <summary>
        /// Reads an operator-entered speed and gates it on 0 &lt; speed &lt;= the axis
        /// parameter speed, the same check V3's ManualScreen runs before every home,
        /// jog and move. Without it any number was accepted and the drive silently ran
        /// at its own configured speed, which reads as the entry being ignored.
        /// A limit of zero means none is configured, so only the positive check applies.
        /// </summary>
        private bool TryReadSpeed(string text, out double speed)
        {
            if (!TryParseNumber(text, out speed) || speed <= 0)
            {
                ShowMessage(SpeedRangeMessage);
                return false;
            }

            double limit = SpeedLimit;
            if (limit > 0 && speed > limit)
            {
                ShowMessage(SpeedRangeMessage + " (" +
                    limit.ToString("0.##", CultureInfo.InvariantCulture) + " mm/s).");
                speed = 0;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Gates the point-to-point iteration count on 0 &lt; count &lt;= the configured
        /// ceiling, the check V3's MovePTP runs against App.config's PtpIterationCount.
        /// Nova enforced only "greater than 0", so a mistyped 99999 was accepted and the
        /// axis cycled for hours with nothing but Stop to get out of it.
        /// </summary>
        private bool TryReadIterations(string text, out int iterations)
        {
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out iterations)
                || iterations <= 0
                || iterations > MaxIterations)
            {
                ShowMessage(IterationRangeMessage);
                iterations = 0;
                return false;
            }

            return true;
        }

        /// <summary>A position is signed and may legitimately be zero.</summary>
        private bool TryReadPosition(string text, out double position)
        {
            if (!TryParseNumber(text, out position))
            {
                ShowMessage("Please enter a valid position.");
                return false;
            }

            return true;
        }

        // Invariant first: the numeric boxes only ever accept "." as the separator,
        // whatever the machine's regional settings say.
        private static bool TryParseNumber(string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        private void ShowMessage(string message)
        {
            if (_dialog != null)
                _dialog.ShowError(message, DialogTitle);
        }

        private void CancelPointToPoint()
        {
            var cts = _ptpCts;
            _ptpCts = null;
            if (cts != null)
            {
                cts.Cancel();
                cts.Dispose();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !IsDisposed)
            {
                if (_monitor != null)
                    _monitor.AxisStatusUpdated -= OnAxisStatusUpdated;
                CancelPointToPoint();
            }
            base.Dispose(disposing);
        }
    }
}
