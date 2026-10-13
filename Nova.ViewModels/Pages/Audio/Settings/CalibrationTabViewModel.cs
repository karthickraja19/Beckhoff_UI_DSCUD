using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Machine.Interfaces;
using Nova.Abstractions.Services;
using Nova.ViewModels.Common;

namespace Nova.ViewModels.Pages.Audio
{
    /// <summary>
    /// The Setting page's Calibration tab: pick a station and a calibration routine, enter the
    /// routine's parameters, and run it with Start / Pause / Stop. The PLC runs the sequence;
    /// the tab shows its step, asks the operator when the sequence waits for a manual check,
    /// and shows what it measured.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>Start is enabled only once the PLC reports homing done, nothing is running, the
    /// operator is Level 2 or 3, and every parameter is entered and saved - so what the PLC
    /// runs with is what the HMI holds. While paused, Start resumes.</item>
    /// <item>Pause while running; Stop while running, paused or waiting for the operator.</item>
    /// <item>One station at a time. The station and routine are locked while a calibration
    /// runs, and follow the PLC's when the tab opens on one already running.</item>
    /// <item>The parameters are kept per station and routine; editing needs Level 2 or 3.</item>
    /// <item>The machine is in manual mode while the tab is on screen, as for the Manual tab,
    /// and the PLC's calibration status is followed only then.</item>
    /// </list>
    /// </remarks>
    public partial class CalibrationTabViewModel : SettingsTabBase, IDisposable
    {
        private readonly ICalibrationService _calibration;
        private readonly IHomingService _homing;
        private readonly IMachineOrchestrator _orchestrator;
        private readonly IDialogService _dialog;
        private readonly IAppSession _session;
        private readonly IAccessLog _accessLog;

        private CalibrationStatus _status = CalibrationStatus.NotLive;
        private IDisposable _following;
        private bool _isShown;
        private bool _isSending;
        private bool _hasSaved;          // the routine has saved parameters for the station
        private bool _disposed;

        public CalibrationTabViewModel(
            ICalibrationService calibration = null,
            IHomingService homing = null,
            IMachineOrchestrator orchestrator = null,
            IDialogService dialog = null,
            IAppSession session = null,
            IAccessLog accessLog = null)
        {
            _calibration = calibration;
            _homing = homing;
            _orchestrator = orchestrator;
            _dialog = dialog;
            _session = session;
            _accessLog = accessLog;

            Stations = new[]
            {
                new CalibrationStationOption(1, "Station 1 (CCD1)"),
                new CalibrationStationOption(2, "Station 2 (CCD2)"),
            };
            Routines = CalibrationRoutines.All;

            StartCommand = new AsyncRelayCommand(StartOrResumeAsync, () => CanStart);
            PauseCommand = new AsyncRelayCommand(() => SendAsync(c => c.PauseAsync(), "Pause", "paused"), () => CanPause);
            StopCommand = new AsyncRelayCommand(() => SendAsync(c => c.StopAsync(), "Stop", "stopped"), () => CanStop);
            AcceptCommand = new AsyncRelayCommand(() => SendAsync(c => c.AnswerPromptAsync(true), "Accept", "accepted the check"), () => CanAnswer);
            RejectCommand = new AsyncRelayCommand(() => SendAsync(c => c.AnswerPromptAsync(false), "Reject", "rejected the check"), () => CanAnswer);
            EditCommand = new RelayCommand(Edit, () => CanEditParameters && HasParameters && !IsEditing);
            SaveCommand = new RelayCommand(Save, () => IsEditing && CanEditParameters);
            CancelCommand = new RelayCommand(LoadParameters, () => IsEditing);
            HomeCommand = new RelayCommand(Home, () => CanHome);

            _selectedStation = Stations[0];
            _selectedRoutine = Routines[0];

            if (_calibration != null)
                _calibration.StatusChanged += OnStatusChanged;
            if (_homing != null)
                _homing.StateChanged += OnHomingChanged;

            LoadParameters();
            RefreshStatus();
        }

        public override string Title => "Calibration";

        public bool IsAvailable => _calibration != null && _calibration.IsAvailable;

        public string UnavailableText => _calibration == null
            ? "Calibration is not available on this build."
            : _calibration.UnavailableReason;


        // ==================================================================
        // Station and routine
        // ==================================================================

        public IReadOnlyList<CalibrationStationOption> Stations { get; }

        public IReadOnlyList<CalibrationRoutine> Routines { get; }

        [ObservableProperty]
        private CalibrationStationOption _selectedStation;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Procedure), nameof(Reference), nameof(HasParameters), nameof(ResultHeaders))]
        private CalibrationRoutine _selectedRoutine;

        partial void OnSelectedStationChanged(CalibrationStationOption value)
        {
            LoadParameters();
            RefreshStatus();
        }

        partial void OnSelectedRoutineChanged(CalibrationRoutine value)
        {
            LoadParameters();
            RefreshStatus();
        }

        /// <summary>Locked while a calibration runs, and while the parameters are being edited.</summary>
        public bool CanChangeSelection => !_status.IsBusy && !_isSending && !IsEditing;

        public IReadOnlyList<string> Procedure => SelectedRoutine != null
            ? SelectedRoutine.Procedure.Select((step, i) => (i + 1) + ". " + step).ToList()
            : (IReadOnlyList<string>)new string[0];

        public string Reference => SelectedRoutine != null ? SelectedRoutine.Reference : string.Empty;


        // ==================================================================
        // Parameters
        // ==================================================================

        public ObservableCollection<CalibrationParameterRow> Parameters { get; } = new ObservableCollection<CalibrationParameterRow>();

        public bool HasParameters => SelectedRoutine != null && SelectedRoutine.Parameters.Count > 0;

        /// <summary>
        /// Edit pressed: the parameter boxes take input, and Save / Cancel replace Edit. Save
        /// works without a change too, so the SOP's values can be saved for a station as they are.
        /// </summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanChangeSelection), nameof(StartBlockedReason), nameof(IsNotEditing))]
        private bool _isEditing;

        public bool IsNotEditing => !IsEditing;

        /// <summary>A value was changed since Edit.</summary>
        [ObservableProperty]
        private bool _isDirty;

        [ObservableProperty]
        private string _savedText = string.Empty;

        /// <summary>Level 2 or 3, and nothing running: Edit is allowed.</summary>
        public bool CanEditParameters => CanEdit && !_status.IsBusy && !_isSending;

        public string AccessHint => CanEdit
            ? "Level " + (int)Level + ": press Edit to change the parameters, then Save. Start runs with the saved values."
            : "Log in at Level 2 or 3 to edit the parameters and run a calibration.";

        /// <summary>Why Edit is disabled; empty when it is enabled.</summary>
        public string EditTooltip => !CanEdit
            ? "Log in at Level 2 or 3 to edit the parameters."
            : _status.IsBusy ? "A calibration is running." : "Edit the parameters for this station and routine.";

        public IRelayCommand EditCommand { get; }

        public IRelayCommand SaveCommand { get; }

        public IRelayCommand CancelCommand { get; }

        private void Edit()
        {
            IsEditing = true;
            IsDirty = false;
            UpdateCommands();
        }

        private void LoadParameters()
        {
            Parameters.Clear();
            _hasSaved = false;
            IsEditing = false;
            if (SelectedRoutine == null || SelectedStation == null)
            {
                IsDirty = false;
                SavedText = string.Empty;
                return;
            }

            CalibrationParameterSet saved = _calibration?.LoadParameters(SelectedStation.Number, SelectedRoutine.Key);
            _hasSaved = saved != null;
            foreach (CalibrationParameter definition in SelectedRoutine.Parameters)
            {
                var row = new CalibrationParameterRow(definition, OnParameterEdited);
                double value;
                row.Load(saved != null && saved.Values != null && saved.Values.TryGetValue(definition.Key, out value)
                    ? value
                    : definition.Default);
                Parameters.Add(row);
            }

            IsDirty = false;
            SavedText = !HasParameters
                ? "This routine takes no parameters."
                : saved == null
                    ? "Not saved for " + SelectedStation.Name + " yet: the values shown are the SOP's, and blank ones must be entered."
                    : "Saved by " + (string.IsNullOrEmpty(saved.SavedBy) ? "-" : saved.SavedBy)
                      + " on " + saved.SavedAt.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture) + ".";
            UpdateCommands();
        }

        private void OnParameterEdited()
        {
            IsDirty = true;
            UpdateCommands();
        }

        private void Save()
        {
            if (_calibration == null || SelectedRoutine == null || SelectedStation == null)
                return;

            CalibrationParameterRow invalid = Parameters.FirstOrDefault(p => p.Value == null);
            if (invalid != null)
            {
                _dialog?.ShowWarning(invalid.Label + ": " + invalid.Error + ".", "Calibration");
                return;
            }

            var values = Parameters.ToDictionary(p => p.Definition.Key, p => p.Value.Value);
            try
            {
                _calibration.SaveParameters(SelectedStation.Number, SelectedRoutine.Key, values, UserName);
            }
            catch (Exception ex)
            {
                _dialog?.ShowError("The parameters could not be saved: " + ex.Message, "Calibration");
                return;
            }

            LogAction("saved " + SelectedRoutine.Name + " parameters for " + SelectedStation.Name + ": "
                      + string.Join(", ", Parameters.Select(p => p.Definition.Key + "=" + p.Text)));
            LoadParameters();
        }


        // ==================================================================
        // Homing - Start needs the PLC's homing done
        // ==================================================================

        public bool HasHoming => _homing != null;

        public bool IsHomed => _homing != null && _homing.ReportsHomed && _homing.IsHomed;

        public bool IsHoming => _homing != null && _homing.IsHoming;

        public string HomingText
        {
            get
            {
                if (_homing == null || !_homing.ReportsHomed)
                    return "Homing is not reported by the PLC (PlcHomingDoneSymbol is not set), so calibration cannot start.";
                if (IsHoming)
                    return "Homing...";
                return IsHomed ? "Machine homed." : "Not homed: home the machine before calibrating.";
            }
        }

        public bool CanHome => _homing != null && _homing.IsAvailable && !IsHoming && !_status.IsBusy;

        public IRelayCommand HomeCommand { get; }

        private void Home()
        {
            try
            {
                _homing.StartHoming();
                LogAction("Homing started (Calibration tab)");
            }
            catch (Exception ex)
            {
                _dialog?.ShowError("Homing could not be started: " + ex.Message, "Homing");
            }

            RefreshStatus();
        }


        // ==================================================================
        // Start / Pause / Stop, and the operator's check
        // ==================================================================

        public IAsyncRelayCommand StartCommand { get; }

        public IAsyncRelayCommand PauseCommand { get; }

        public IAsyncRelayCommand StopCommand { get; }

        public IAsyncRelayCommand AcceptCommand { get; }

        public IAsyncRelayCommand RejectCommand { get; }

        public bool IsPaused => _status.IsLive && _status.State == CalibrationState.Paused;

        public string StartCaption => IsPaused ? "Resume" : "Start";

        public bool CanStart => StartBlockedReason.Length == 0;

        /// <summary>Why Start is disabled, for its tooltip and the hint under it; empty when it is enabled.</summary>
        public string StartBlockedReason
        {
            get
            {
                if (!IsAvailable)
                    return UnavailableText;
                if (!_status.IsLive)
                    return _status.NotLiveReason.Length > 0 ? _status.NotLiveReason : "No calibration status from the PLC.";
                if (_isSending)
                    return "Sending to the PLC...";
                if (!CanEdit)
                    return "Log in at Level 2 or 3 to run a calibration.";
                if (IsPaused)
                    return string.Empty;
                if (_status.IsBusy)
                    return "A calibration is running.";
                if (_homing == null || !_homing.ReportsHomed)
                    return "The PLC does not report homing (PlcHomingDoneSymbol is not set).";
                if (IsHoming)
                    return "Wait for homing to finish.";
                if (!IsHomed)
                    return "Home the machine first.";
                if (SelectedStation == null || SelectedRoutine == null)
                    return "Select a station and a routine.";
                if (IsEditing)
                    return "Save or cancel the parameter edit first.";
                CalibrationParameterRow invalid = Parameters.FirstOrDefault(p => p.Value == null);
                if (invalid != null)
                    return invalid.Label + ": " + invalid.Error + ".";
                if (!_hasSaved && HasParameters)
                    return "Save the parameters for " + SelectedStation.Name + " first.";
                return string.Empty;
            }
        }

        public bool CanPause => IsAvailable && !_isSending && _status.IsLive && _status.State == CalibrationState.Running;

        public bool CanStop => IsAvailable && !_isSending && _status.IsBusy;

        public bool IsWaitingOperator => _status.IsLive && _status.State == CalibrationState.WaitingOperator;

        public bool CanAnswer => IsAvailable && !_isSending && IsWaitingOperator;

        public string Prompt => _status.Prompt;

        private Task StartOrResumeAsync()
        {
            if (IsPaused)
                return SendAsync(c => c.ResumeAsync(), "Resume", "resumed");

            CalibrationRoutine routine = SelectedRoutine;
            CalibrationStationOption station = SelectedStation;
            double[] values = Parameters.Select(p => p.Value ?? 0).ToArray();
            return SendAsync(c => c.StartAsync(station.Number, routine.Number, values), "Start",
                "started " + routine.Name + " on " + station.Name
                + (values.Length > 0 ? " (" + string.Join(", ", Parameters.Select(p => p.Definition.Key + "=" + p.Text)) + ")" : string.Empty));
        }

        private async Task SendAsync(Func<ICalibrationService, Task> send, string button, string action)
        {
            if (_calibration == null)
                return;

            _isSending = true;
            RefreshStatus();
            try
            {
                await send(_calibration);
                LogAction("Calibration " + action);
            }
            catch (Exception ex)
            {
                _dialog?.ShowError(button + " could not be sent to the PLC: " + ex.Message, "Calibration");
            }
            finally
            {
                _isSending = false;
                RefreshStatus();
            }
        }


        // ==================================================================
        // The PLC's status and results
        // ==================================================================

        public bool IsLive => _status.IsLive;

        public bool IsBusy => _status.IsBusy;

        public string StateText
        {
            get
            {
                if (!IsAvailable)
                    return "Not available";
                if (!_status.IsLive)
                    return "No PLC status";

                switch (_status.State)
                {
                    case CalibrationState.Idle: return "Idle";
                    case CalibrationState.Running: return "Running";
                    case CalibrationState.Paused: return "Paused";
                    case CalibrationState.WaitingOperator: return "Waiting for operator";
                    case CalibrationState.Done: return _status.ResultOk ? "Done - OK" : "Done - NG";
                    case CalibrationState.Failed: return "Failed";
                    case CalibrationState.Stopped: return "Stopped";
                    default: return "Unknown state " + (int)_status.State;
                }
            }
        }

        /// <summary>The state's colour on screen.</summary>
        public bool IsStateOk => _status.IsLive && _status.State == CalibrationState.Done && _status.ResultOk;

        public bool IsStateFail => _status.IsLive
            && (_status.State == CalibrationState.Failed || (_status.State == CalibrationState.Done && !_status.ResultOk));

        public bool IsStateActive => _status.IsBusy;

        /// <summary>What the PLC is doing, and on which station and routine.</summary>
        public string RunText
        {
            get
            {
                if (!_status.IsLive)
                    return _status.NotLiveReason;
                if (_status.State == CalibrationState.Idle)
                    return "Nothing has run since the PLC started.";

                CalibrationRoutine routine = Routines.FirstOrDefault(r => r.Number == _status.Routine);
                string what = (routine != null ? routine.Name : "Routine " + _status.Routine) + ", Station " + _status.Station;
                string step = _status.Step > 0
                    ? "Step " + _status.Step + (_status.StepText.Length > 0 ? ": " + _status.StepText : string.Empty)
                    : _status.StepText;
                return step.Length > 0 ? what + " - " + step : what;
            }
        }

        public IReadOnlyList<string> ResultHeaders => SelectedRoutine == null
            ? (IReadOnlyList<string>)new string[0]
            : SelectedRoutine.ResultColumns.Select(c => c.Unit.Length > 0 ? c.Header + " (" + c.Unit + ")" : c.Header).ToList();

        public ObservableCollection<CalibrationResultRow> ResultRows { get; } = new ObservableCollection<CalibrationResultRow>();

        [ObservableProperty]
        private string _resultNote = string.Empty;

        private void OnStatusChanged(object sender, EventArgs e) => OnUi(RefreshStatus);

        private void OnHomingChanged(object sender, EventArgs e) => OnUi(RefreshStatus);

        private void RefreshStatus()
        {
            if (_disposed)
                return;

            _status = _calibration != null ? _calibration.Status : CalibrationStatus.NotLive;

            // Opened on a calibration already under way: show the PLC's station and routine.
            if (_status.IsBusy)
            {
                CalibrationStationOption station = Stations.FirstOrDefault(s => s.Number == _status.Station);
                CalibrationRoutine routine = Routines.FirstOrDefault(r => r.Number == _status.Routine);
                if (station != null && station != SelectedStation)
                    SelectedStation = station;
                if (routine != null && routine != SelectedRoutine)
                    SelectedRoutine = routine;
            }

            FillResults();

            OnPropertyChanged(nameof(IsLive));
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(IsPaused));
            OnPropertyChanged(nameof(StartCaption));
            OnPropertyChanged(nameof(StateText));
            OnPropertyChanged(nameof(IsStateOk));
            OnPropertyChanged(nameof(IsStateFail));
            OnPropertyChanged(nameof(IsStateActive));
            OnPropertyChanged(nameof(RunText));
            OnPropertyChanged(nameof(IsWaitingOperator));
            OnPropertyChanged(nameof(Prompt));
            OnPropertyChanged(nameof(IsHomed));
            OnPropertyChanged(nameof(IsHoming));
            OnPropertyChanged(nameof(HomingText));
            OnPropertyChanged(nameof(CanChangeSelection));
            OnPropertyChanged(nameof(CanEditParameters));
            OnPropertyChanged(nameof(StartBlockedReason));
            UpdateCommands();
        }

        /// <summary>The PLC's results, when they are for the routine and station on screen.</summary>
        private void FillResults()
        {
            ResultRows.Clear();
            CalibrationRoutine routine = SelectedRoutine;
            if (routine == null)
                return;

            bool ours = _status.IsLive
                && _status.State != CalibrationState.Idle
                && SelectedStation != null
                && _status.Station == SelectedStation.Number
                && _status.Routine == routine.Number;
            IReadOnlyList<double> results = ours ? _status.Results : null;

            int columns = routine.ResultColumns.Count;
            for (int r = 0; r < routine.ResultRows.Count; r++)
            {
                var cells = new List<CalibrationResultCell>(columns);
                for (int c = 0; c < columns; c++)
                {
                    int index = r * columns + c;
                    if (results == null || index >= results.Count)
                    {
                        cells.Add(new CalibrationResultCell("-", false));
                        continue;
                    }

                    double value = results[index];
                    cells.Add(routine.ResultColumns[c].IsPass
                        ? new CalibrationResultCell(value != 0 ? "OK" : "NG", value == 0)
                        : new CalibrationResultCell(value.ToString("0.000", CultureInfo.InvariantCulture), false));
                }

                ResultRows.Add(new CalibrationResultRow(routine.ResultRows[r], cells));
            }

            ResultNote = ours
                ? (_status.IsBusy ? "Measuring - values fill in as the PLC reports them." : "From the last run on this station.")
                : "No result from the PLC for this routine on this station yet.";
        }


        // ==================================================================
        // On screen
        // ==================================================================

        /// <summary>The tab came on screen: manual mode on, and the PLC's calibration status is followed.</summary>
        public override void OnShown()
        {
            if (_isShown)
                return;

            _isShown = true;
            _orchestrator?.SetManualMode(true);
            if (_calibration != null)
                _following = _calibration.FollowStatus();
            if (!IsEditing)
                LoadParameters();
            RefreshStatus();
        }

        /// <summary>The tab left the screen: unsaved edits dropped, status let go, manual mode off.</summary>
        public override void OnHidden()
        {
            if (!_isShown)
                return;

            _isShown = false;
            if (IsEditing)
                LoadParameters();
            _following?.Dispose();
            _following = null;
            _orchestrator?.SetManualMode(false);
        }

        protected override void OnLevelChanged()
        {
            if (!CanEdit && IsEditing)
                LoadParameters();

            OnPropertyChanged(nameof(AccessHint));
            RefreshStatus();
        }

        private void UpdateCommands()
        {
            OnPropertyChanged(nameof(CanEditParameters));
            OnPropertyChanged(nameof(CanHome));
            OnPropertyChanged(nameof(EditTooltip));
            StartCommand.NotifyCanExecuteChanged();
            PauseCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();
            AcceptCommand.NotifyCanExecuteChanged();
            RejectCommand.NotifyCanExecuteChanged();
            SaveCommand.NotifyCanExecuteChanged();
            EditCommand.NotifyCanExecuteChanged();
            CancelCommand.NotifyCanExecuteChanged();
            HomeCommand.NotifyCanExecuteChanged();
        }

        private string UserName => _session != null ? _session.UserName : string.Empty;

        /// <summary>Slide 44: "All L1/L2/L3 actions are recorded in machine log".</summary>
        private void LogAction(string action)
        {
            if (_accessLog != null && _session != null)
                _accessLog.Action(_session.UserName, (int)Level, action);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            OnHidden();
            _disposed = true;

            if (_calibration != null)
                _calibration.StatusChanged -= OnStatusChanged;
            if (_homing != null)
                _homing.StateChanged -= OnHomingChanged;
        }
    }
}
