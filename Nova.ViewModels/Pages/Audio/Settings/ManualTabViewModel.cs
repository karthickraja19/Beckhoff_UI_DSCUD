using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Machine.Interfaces;
using Nova.Abstractions.Services;
using Nova.ViewModels.Common;

namespace Nova.ViewModels.Pages.Audio
{
    /// <summary>
    /// The Setting page's Manual tab: the manual-operation GUI (the Manual screens' device
    /// tabs), with the Homing button and the running log beside it.
    /// </summary>
    /// <remarks>
    /// The machine is in manual mode while this tab is on screen - the PLC acts on the HMI's
    /// device commands only in manual mode - and taken out of it as soon as the operator
    /// moves to another tab or page. The manual GUI follows its devices' live status only
    /// while the tab is on screen too.
    /// </remarks>
    public partial class ManualTabViewModel : SettingsTabBase, IDisposable
    {
        private readonly IHomingService _homing;
        private readonly IMachineOrchestrator _orchestrator;
        private readonly IDialogService _dialog;
        private readonly IAppSession _session;
        private readonly IAccessLog _accessLog;
        private bool _isShown;
        private bool _disposed;

        public override string Title => "Manual";

        /// <summary>The manual-operation GUI. Null on a build with no manual screens.</summary>
        public PageViewModel Gui { get; }

        /// <summary>Whether there is a manual GUI to show.</summary>
        public bool HasGui => Gui != null;

        /// <summary>
        /// Live running log beside the manual GUI, so an operator driving the station by
        /// hand can see what the machine just did.
        /// </summary>
        public RunningLogViewModel RunningLog { get; }

        public ManualTabViewModel(
            PageViewModel gui = null,
            IRunningLogService runningLog = null,
            IHomingService homing = null,
            IMachineOrchestrator orchestrator = null,
            IDialogService dialog = null,
            IAppSession session = null,
            IAccessLog accessLog = null)
        {
            Gui = gui;
            RunningLog = new RunningLogViewModel(runningLog);
            _homing = homing;
            _orchestrator = orchestrator;
            _dialog = dialog;
            _session = session;
            _accessLog = accessLog;

            if (_homing != null)
            {
                _isHoming = _homing.IsHoming;
                _homing.StateChanged += OnHomingStateChanged;
                _homing.HomingCompleted += OnHomingCompleted;
            }
        }

        /// <summary>The tab came on screen: manual mode on, and the manual GUI follows its devices.</summary>
        public override void OnShown()
        {
            if (_isShown)
                return;

            _isShown = true;
            _orchestrator?.SetManualMode(true);
            Gui?.OnNavigatedTo();
        }

        /// <summary>
        /// The tab left the screen: manual mode off, and the manual GUI lets its devices go.
        /// The GUI is a DI singleton, so a grid left unlocked on it would stay unlocked;
        /// leaving the tab ends any edit standing on it.
        /// </summary>
        public override void OnHidden()
        {
            if (!_isShown)
                return;

            _isShown = false;
            Gui?.OnNavigatedFrom();
            _orchestrator?.SetManualMode(false);
        }


        // ==================================================================
        // Homing button (above the Running Log)
        // ==================================================================

        /// <summary>Whether there is a homing service behind the button.</summary>
        public bool HasHoming => _homing != null;

        /// <summary>The PLC is homing: from the command until it reports homing done.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanHome))]
        [NotifyPropertyChangedFor(nameof(HomingCaption))]
        private bool _isHoming;

        /// <summary>The button's text.</summary>
        public string HomingCaption => IsHoming ? "Homing..." : "Homing";

        /// <summary>Tooltip: why the button is disabled, or what it does.</summary>
        public string HomingTooltip => _homing == null || _homing.IsAvailable
            ? "Home every component (the PLC runs the homing sequence)"
            : _homing.UnavailableReason;

        public bool CanHome => _homing != null && _homing.IsAvailable && !IsHoming;

        /// <summary>Sets the PLC's home command bit TRUE. The PLC does the homing.</summary>
        [RelayCommand]
        private void HomeMachine()
        {
            try
            {
                _homing.StartHoming();
                LogAction("Homing started");
            }
            catch (Exception ex)
            {
                _dialog?.ShowError("Homing could not be started: " + ex.Message, "Homing");
            }
        }

        // The homing events come from the ADS thread; the button and the pop-up belong to the UI thread.
        private void OnHomingStateChanged(object sender, EventArgs e) => OnUi(() => IsHoming = _homing.IsHoming);

        private void OnHomingCompleted(object sender, EventArgs e)
        {
            OnUi(() =>
            {
                IsHoming = false;
                _dialog?.ShowInfo("Homing done.", "Homing");
            });
        }

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

            _disposed = true;
            OnHidden();

            if (_homing != null)
            {
                _homing.StateChanged -= OnHomingStateChanged;
                _homing.HomingCompleted -= OnHomingCompleted;
            }

            RunningLog.Dispose();
        }
    }
}
