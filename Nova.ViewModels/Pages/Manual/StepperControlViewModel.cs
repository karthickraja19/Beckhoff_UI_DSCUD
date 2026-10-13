using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Hardware;
using Nova.Abstractions.Services;
using Nova.ViewModels.Common;

namespace Nova.ViewModels.Pages.Manual
{
    /// <summary>
    /// ViewModel backing the "Stepper" tab of the Manual screen. Mirrors the V3
    /// conveyor-stepper behaviour (motor select + Forward/Reverse/Stop): commands
    /// delegate to the selected <see cref="IStepper"/> via
    /// <see cref="IStepperService"/>, and live read-back arrives through
    /// <see cref="IAxisMonitorService"/> events (steppers share the axis snapshot) - for
    /// the selected motor only, and only while the tab is on screen.
    /// </summary>
    public partial class StepperControlViewModel : ViewModelBase, IActivatableTab
    {
        private readonly IStepperService _stepperService;
        private readonly IAxisMonitorService _monitor;
        private readonly IDialogService _dialog;

        private bool _isActive;

        /// <summary>The selected motor's live status, followed while the tab is on screen.</summary>
        private IDisposable _following;

        // ---- Stepper selection --------------------------------------------
        public ObservableCollection<string> Motors { get; }

        [ObservableProperty]
        private string _selectedMotor;

        // ---- Motion parameters --------------------------------------------
        [ObservableProperty]
        private string _speed;

        // ---- Live read-back values ----------------------------------------
        [ObservableProperty]
        private double _actualSpeed;

        // Short, human-readable rollup of the device state ("Ready" / "Alarm" / "Running").
        [ObservableProperty]
        private string _statusText = "Ready";

        // ---- Device status (data-driven indicator list) -------------------
        public ObservableCollection<AxisStatusItem> DeviceStatus { get; }

        // An I/O stepper reports two things: whether it is running and whether it is in error.
        private readonly AxisStatusItem _alarmStatus;
        private readonly AxisStatusItem _runningStatus;

        public StepperControlViewModel(IStepperService stepperService, IAxisMonitorService monitor,
            IDialogService dialog = null)
        {
            _stepperService = stepperService;
            _monitor = monitor;
            _dialog = dialog;

            Motors = new ObservableCollection<string>();
            // IO steppers only, from Data\Steppers.json. Axis-based steppers drive a real
            // IAxis and are listed on the Axis screen instead, where position, homing and
            // jog actually apply; this screen offers Forward/Reverse/Stop, which is all an
            // IO stepper has. The list is empty when every stepper is axis-based.
            var all = _stepperService?.GetAllSteppers();
            if (all != null)
            {
                foreach (var stepper in all.Where(s => !s.IsAxisBased))
                    Motors.Add(stepper.ComponentId);
            }

            _selectedMotor = Motors.Count > 0 ? Motors[0] : null;

            _alarmStatus = new AxisStatusItem("In Alarm", isAlarm: true);
            _runningStatus = new AxisStatusItem("Running");

            DeviceStatus = new ObservableCollection<AxisStatusItem>
            {
                _alarmStatus,
                _runningStatus,
            };

            if (_monitor != null)
                _monitor.AxisStatusUpdated += OnStatusUpdated;
        }

        private IStepper CurrentStepper => _stepperService?.GetStepper(SelectedMotor);

        // Raised on the monitor's background thread: scalar INPC setters only here
        // (WPF marshals those); the DeviceStatus collection itself is never mutated.
        //
        // Every indicator now comes straight off the snapshot, so they refresh on the
        // monitor's 250 ms tick for as long as the conveyor runs. They used to be set
        // once from the button handler and then never updated for an IO stepper, because
        // MasterStepperAdapter had nothing to report; the adapter now latches the
        // commanded direction and speed and reports them, so there is one source of
        // truth here whichever kind of stepper is selected.
        private void OnStatusUpdated(object sender, AxisStatusSnapshot s)
        {
            if (s.ComponentId != SelectedMotor)
                return;

            ActualSpeed = s.ActualSpeed;
            _alarmStatus.IsActive = s.IsInError;
            _runningStatus.IsActive = s.IsMoving;

            StatusText = s.IsInError ? "Alarm" : (s.IsMoving ? "Running" : "Ready");
        }

        /// <summary>Blanks the readouts on a motor change so the previous motor's state
        /// is not shown against the new one for the poll or two before it reports.</summary>
        private void ClearMotionIndicators()
        {
            _alarmStatus.IsActive = false;
            _runningStatus.IsActive = false;
            ActualSpeed = 0;
            StatusText = "Ready";
        }

        partial void OnSelectedMotorChanged(string value)
        {
            ClearMotionIndicators();
            FollowSelectedMotor();
        }

        public void Activate()
        {
            if (_isActive)
                return;

            _isActive = true;
            ClearMotionIndicators();
            FollowSelectedMotor();
        }

        public void Deactivate()
        {
            if (!_isActive)
                return;

            _isActive = false;
            _following?.Dispose();
            _following = null;
        }

        /// <summary>
        /// Follows the live status of the selected motor, and lets go of the one selected
        /// before. Its values arrive through the monitor once the PLC sends them.
        /// </summary>
        private void FollowSelectedMotor()
        {
            if (!_isActive)
                return;

            IDisposable previous = _following;
            _following = (CurrentStepper as ILiveStatus)?.FollowStatus();
            previous?.Dispose();
        }

        // ---- Commands -----------------------------------------------------

        [RelayCommand]
        private async Task Forward()
        {
            var stepper = CurrentStepper;
            if (stepper == null) return;
            if (!TryReadSpeed(out double speed)) return;
            await stepper.EnableAsync();
            await stepper.ForwardAsync(speed);
        }

        [RelayCommand]
        private async Task Reverse()
        {
            var stepper = CurrentStepper;
            if (stepper == null) return;
            if (!TryReadSpeed(out double speed)) return;
            await stepper.EnableAsync();
            await stepper.ReverseAsync(speed);
        }

        [RelayCommand]
        private async Task Stop()
        {
            var stepper = CurrentStepper;
            if (stepper == null) return;
            await stepper.StopAsync();
            ClearMotionIndicators();
        }

        /// <summary>
        /// A blank speed box means "use the configured default" (-1), which is what
        /// IStepper documents. Anything typed must still be a positive number — letters
        /// used to fall through TryParse as 0 and then be turned into -1, so a typo ran
        /// the conveyor at its default speed with no warning.
        /// </summary>
        private bool TryReadSpeed(out double speed)
        {
            speed = -1;
            if (string.IsNullOrWhiteSpace(Speed))
                return true;

            double entered;
            bool parsed = double.TryParse(Speed, NumberStyles.Float, CultureInfo.InvariantCulture, out entered)
                || double.TryParse(Speed, NumberStyles.Float, CultureInfo.CurrentCulture, out entered);

            if (!parsed || entered <= 0)
            {
                if (_dialog != null)
                    _dialog.ShowError("Please enter a speed greater than 0, or leave it blank to use the configured speed.", "Manual Screen");
                return false;
            }

            speed = entered;
            return true;
        }

        [RelayCommand]
        private async Task ResetAlarm()
        {
            var stepper = CurrentStepper;
            if (stepper == null) return;
            await stepper.ResetAlarmAsync();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !IsDisposed)
            {
                if (_monitor != null)
                    _monitor.AxisStatusUpdated -= OnStatusUpdated;
                Deactivate();
            }
            base.Dispose(disposing);
        }
    }
}
