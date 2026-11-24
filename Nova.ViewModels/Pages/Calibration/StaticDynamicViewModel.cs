using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Machine.Enums;
using Nova.Abstractions.Machine.Interfaces;
using Nova.Abstractions.Services;
using Nova.ViewModels.Common;

namespace Nova.ViewModels.Pages.Calibration
{
    /// <summary>A camera in the Choose Camera box.</summary>
    public sealed class StaticDynamicCamera : ObservableObject
    {
        private readonly Action<StaticDynamicCamera> _selected;
        private bool _isSelected;

        public StaticDynamicCamera(int number, Action<StaticDynamicCamera> selected)
        {
            Number = number;
            _selected = selected;
        }

        public int Number { get; }

        public string Name => "CCD " + Number;

        /// <summary>The radio button. Picking it tells the tab; the tab sets the others.</summary>
        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                if (!SetProperty(ref _isSelected, value))
                    return;

                if (value)
                    _selected?.Invoke(this);
            }
        }

        /// <summary>Set by the tab itself: no callback.</summary>
        internal void Show(bool selected)
        {
            SetProperty(ref _isSelected, selected, nameof(IsSelected));
        }
    }

    /// <summary>
    /// Config &gt; Calibration &gt; Static &amp; Dynamic: the CCD verification the PLC's Static_Seq and
    /// Dynamic_Seq run. The HMI picks the mode, the camera and the iteration count and presses
    /// Start / End / Stop; the PLC moves the camera to its recipe position, sends the vision
    /// command, logs each answer and reports what it is doing - this tab shows that and nothing
    /// it invented.
    /// </summary>
    /// <remarks>
    /// The mode is the PLC's nMode and the main sequence reads it for every unit: while it is
    /// Static or Dynamic the dispensing station skips dispensing, so the tab always shows the
    /// mode the PLC holds, asks before leaving Off, and offers Off to give the machine back to
    /// production. A change of mode, camera or iteration is written to the PLC straight away
    /// (the main sequence uses them without an HMI Start); they are locked while a run is active.
    /// A run needs the machine in AUTO; when it is not, the PLC answers so in its status.
    /// </remarks>
    public partial class StaticDynamicViewModel : ObservableObject, IActivatableTab
    {
        private readonly IStaticDynamicService _service;
        private readonly IDialogService _dialog;
        private readonly IMachineOrchestrator _orchestrator;
        private readonly SynchronizationContext _ui = SynchronizationContext.Current;

        private IDisposable _following;
        private bool _mirroring;      // showing what the PLC holds: nothing is written back
        private bool _synced;         // the settings were taken from the PLC since the tab came into view

        public StaticDynamicViewModel(IStaticDynamicService service, IDialogService dialog, IMachineOrchestrator orchestrator = null)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));
            _orchestrator = orchestrator;

            for (int i = 1; i <= _service.CameraCount; i++)
                Cameras.Add(new StaticDynamicCamera(i, OnCameraPicked));
            Cameras[0].Show(true);

            _service.StatusChanged += OnStatusChanged;
            if (_orchestrator != null)
                _orchestrator.OnMachineStatusChanged += OnMachineStatusChanged;

            ShowStatus();
        }

        /// <summary>Tab title in the Calibration tab strip.</summary>
        public string Title => "Static & Dynamic";

        public string Icon => "Play";

        public ObservableCollection<StaticDynamicCamera> Cameras { get; } = new ObservableCollection<StaticDynamicCamera>();


        // ==================================================================
        // The settings (the PLC's nMode, nCcd, nIteration)
        // ==================================================================

        private StaticDynamicMode _mode = StaticDynamicMode.Off;

        public StaticDynamicMode Mode => _mode;

        public bool IsOff
        {
            get { return _mode == StaticDynamicMode.Off; }
            set { if (value) PickMode(StaticDynamicMode.Off); }
        }

        public bool IsStatic
        {
            get { return _mode == StaticDynamicMode.Static; }
            set { if (value) PickMode(StaticDynamicMode.Static); }
        }

        public bool IsDynamic
        {
            get { return _mode == StaticDynamicMode.Dynamic; }
            set { if (value) PickMode(StaticDynamicMode.Dynamic); }
        }

        /// <summary>The mode is Static or Dynamic: the machine does not dispense until it is Off.</summary>
        public bool ProductionSuspended => _mode != StaticDynamicMode.Off;

        /// <summary>Number of vision commands per run, as typed.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanStart))]
        private string _iteration = "1";

        public StaticDynamicCamera SelectedCamera => Cameras.FirstOrDefault(c => c.IsSelected);

        /// <summary>The settings can be changed: the PLC is there and no run is active.</summary>
        public bool CanEdit => _service.IsAvailable && IsLive && !IsBusy;

        private void PickMode(StaticDynamicMode mode)
        {
            if (mode == _mode)
                return;

            if (_mirroring)
            {
                SetMode(mode);
                return;
            }

            // Not inside the radio button's own binding update: asking, or putting the buttons back, is done after it.
            Post(() => ApplyPickedMode(mode));
        }

        private void ApplyPickedMode(StaticDynamicMode mode)
        {
            if (mode == _mode)
                return;

            if (!CanEdit)
            {
                RefreshMode();     // put the radio buttons back
                return;
            }

            if (_mode == StaticDynamicMode.Off && mode != StaticDynamicMode.Off
                && !_dialog.Confirm("Selecting " + mode + " makes the machine skip dispensing for every unit until the mode is set back to Off. Continue?", Title))
            {
                RefreshMode();
                return;
            }

            SetMode(mode);
            _ = Write();
        }

        private void SetMode(StaticDynamicMode mode)
        {
            _mode = mode;
            RefreshMode();
            OnPropertyChanged(nameof(Mode));
            OnPropertyChanged(nameof(ProductionSuspended));
            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(ModeText));
        }

        /// <summary>Raises the radio buttons so the one that is on shows.</summary>
        private void RefreshMode()
        {
            OnPropertyChanged(nameof(IsOff));
            OnPropertyChanged(nameof(IsStatic));
            OnPropertyChanged(nameof(IsDynamic));
        }

        private void OnCameraPicked(StaticDynamicCamera picked)
        {
            if (_mirroring)
            {
                foreach (StaticDynamicCamera camera in Cameras.Where(c => !ReferenceEquals(c, picked)))
                    camera.Show(false);
                return;
            }

            foreach (StaticDynamicCamera camera in Cameras.Where(c => !ReferenceEquals(c, picked)))
                camera.Show(false);

            if (!CanEdit)
            {
                // Not allowed now: put the PLC's camera back, after this binding update.
                int ccd = _service.Status != null && _service.Status.Ccd >= 1 ? _service.Status.Ccd : 1;
                Post(() => ShowCamera(ccd));
                return;
            }

            _ = Write();
        }

        private void ShowCamera(int number)
        {
            StaticDynamicCamera target = Cameras.FirstOrDefault(c => c.Number == number) ?? Cameras[0];
            foreach (StaticDynamicCamera camera in Cameras)
                camera.Show(ReferenceEquals(camera, target));
        }

        partial void OnIterationChanged(string value)
        {
            if (_mirroring)
                return;

            ShowStatus();
            if (CanEdit && TryIteration(out int count) && count > 0)
                _ = Write();
        }

        private bool TryIteration(out int count)
        {
            return int.TryParse((Iteration ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out count)
                && count >= 1 && count <= short.MaxValue;
        }

        /// <summary>Writes the settings on screen to the PLC.</summary>
        private async Task Write()
        {
            StaticDynamicCamera camera = SelectedCamera;
            int count;
            if (camera == null || !TryIteration(out count))
                return;

            try
            {
                await _service.ApplyAsync(_mode, camera.Number, count);
            }
            catch (Exception ex)
            {
                _dialog.ShowError("The settings could not be written to the PLC: " + ex.Message, Title);
            }
        }


        // ==================================================================
        // Run buttons
        // ==================================================================

        /// <summary>The PLC is there, nothing runs, the mode is Static or Dynamic and the iteration count is valid.</summary>
        public bool CanStart => _service.IsAvailable && IsLive && !IsBusy && _mode != StaticDynamicMode.Off && TryIteration(out _);

        /// <summary>End and Stop only mean something while a run is active.</summary>
        public bool CanFinish => IsLive && IsBusy;

        [RelayCommand]
        private async Task Start()
        {
            StaticDynamicCamera camera = SelectedCamera;
            int count;
            if (!CanStart || camera == null || !TryIteration(out count))
                return;

            if (!_dialog.Confirm("Start the " + _mode + " test on " + camera.Name + ", " + count + " time(s)? The machine will move.", Title))
                return;

            await Send(() => _service.StartAsync(_mode, camera.Number, count));
        }

        [RelayCommand]
        private Task End()
        {
            return Send(() => _service.EndAsync());
        }

        [RelayCommand]
        private Task Stop()
        {
            return Send(() => _service.StopAsync());
        }

        private async Task Send(Func<Task> command)
        {
            try
            {
                await command();
            }
            catch (Exception ex)
            {
                _dialog.ShowError(ex.Message, Title);
            }
        }


        // ==================================================================
        // What the PLC reports
        // ==================================================================

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanEdit), nameof(CanStart), nameof(CanFinish))]
        private bool _isLive;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanEdit), nameof(CanStart), nameof(CanFinish))]
        private bool _isBusy;

        /// <summary>CURRENT STATUS: the PLC's text, or why there is none.</summary>
        [ObservableProperty]
        private string _statusText = string.Empty;

        /// <summary>"Offline", "Idle", "Running", "Done" or "Error" - the view colours the status with it.</summary>
        [ObservableProperty]
        private string _statusKind = "Offline";

        /// <summary>Vision commands answered in this run, "2 of 5".</summary>
        [ObservableProperty]
        private string _progressText = string.Empty;

        /// <summary>The last X / Y / Z the vision answered, mm.</summary>
        [ObservableProperty]
        private string _visionX = "-";

        [ObservableProperty]
        private string _visionY = "-";

        [ObservableProperty]
        private string _visionZ = "-";

        /// <summary>What the PLC will run, in words, under the radio buttons.</summary>
        public string ModeText
        {
            get
            {
                switch (_mode)
                {
                    case StaticDynamicMode.Static:
                        return "Static is on in the PLC: the machine does not dispense. Select Off for production.";
                    case StaticDynamicMode.Dynamic:
                        return "Dynamic is on in the PLC: the machine does not dispense. Select Off for production.";
                    default:
                        return "Off: the machine dispenses as normal.";
                }
            }
        }

        /// <summary>The machine is not running its auto sequence, which a run needs (the PLC also says so).</summary>
        public bool NeedsAuto => _orchestrator != null && !_orchestrator.IsAutoRunning && !IsBusy;

        public string AutoHint
        {
            get
            {
                if (_orchestrator == null)
                    return string.Empty;

                return "Machine: " + _orchestrator.CurrentStatus + ". A run needs AUTO - turn Manual mode off and press START on the indicator bar.";
            }
        }

        /// <summary>Follows the PLC's status while the tab is on screen.</summary>
        public void Activate()
        {
            _synced = false;
            if (_following == null && _service.IsAvailable)
                _following = _service.FollowStatus();

            ShowStatus();
        }

        public void Deactivate()
        {
            IDisposable following = _following;
            _following = null;
            if (following != null)
                following.Dispose();
        }

        /// <summary>Raised on any thread.</summary>
        private void OnStatusChanged(object sender, EventArgs e)
        {
            Post(ShowStatus);
        }

        private void OnMachineStatusChanged(MachineStatus status)
        {
            Post(() =>
            {
                OnPropertyChanged(nameof(NeedsAuto));
                OnPropertyChanged(nameof(AutoHint));
            });
        }

        private void Post(Action action)
        {
            if (_ui == null)
                action();
            else
                _ui.Post(_ => action(), null);
        }

        private void ShowStatus()
        {
            if (!_service.IsAvailable)
            {
                IsLive = false;
                IsBusy = false;
                StatusKind = "Offline";
                StatusText = _service.UnavailableReason;
                ProgressText = string.Empty;
                return;
            }

            StaticDynamicStatus status = _service.Status ?? StaticDynamicStatus.NotLive;
            IsLive = status.IsLive;
            IsBusy = status.IsLive && status.IsBusy;

            if (!status.IsLive)
            {
                StatusKind = "Offline";
                StatusText = string.IsNullOrEmpty(status.NotLiveReason) ? "Waiting for the PLC." : status.NotLiveReason;
                ProgressText = string.Empty;
                VisionX = VisionY = VisionZ = "-";
                _synced = false;
                OnPropertyChanged(nameof(CanEdit));
                OnPropertyChanged(nameof(CanStart));
                return;
            }

            MirrorSettings(status);

            StatusKind = status.IsBusy ? "Running" : status.IsError ? "Error" : status.IsDone ? "Done" : "Idle";
            string message = (status.Message ?? string.Empty).Trim();
            StatusText = message.Length > 0 ? message : "Ready";

            int wanted;
            ProgressText = (status.IsBusy || status.IsDone || status.IsError) && status.Count > 0
                ? status.Count + " of " + (TryIteration(out wanted) ? wanted : status.Iteration) + " vision commands"
                : string.Empty;

            VisionX = Format(status.VisionX, status.Count);
            VisionY = Format(status.VisionY, status.Count);
            VisionZ = Format(status.VisionZ, status.Count);

            OnPropertyChanged(nameof(NeedsAuto));
            OnPropertyChanged(nameof(AutoHint));
            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(CanStart));
        }

        /// <summary>No answer yet in this run (or since the PLC started): nothing to show.</summary>
        private static string Format(double value, int count)
        {
            return count > 0 ? value.ToString("0.000", CultureInfo.InvariantCulture) : "-";
        }

        /// <summary>
        /// The mode always follows the PLC. The camera and the iteration count are taken from it
        /// once each time the tab comes into view, so a half-typed count is not overwritten.
        /// </summary>
        private void MirrorSettings(StaticDynamicStatus status)
        {
            _mirroring = true;
            try
            {
                if (status.Mode != _mode)
                    SetMode(status.Mode);

                if (!_synced)
                {
                    _synced = true;
                    if (status.Ccd >= 1)
                        ShowCamera(status.Ccd);
                    if (status.Iteration >= 1)
                        Iteration = status.Iteration.ToString(CultureInfo.InvariantCulture);
                }
            }
            finally
            {
                _mirroring = false;
            }
        }
    }
}
