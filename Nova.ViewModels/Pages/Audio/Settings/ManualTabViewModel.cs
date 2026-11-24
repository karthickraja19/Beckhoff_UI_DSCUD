using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Machine.Enums;
using Nova.Abstractions.Machine.Interfaces;
using Nova.Abstractions.Services;
using Nova.ViewModels.Common;
using Nova.ViewModels.Pages.Manual;

namespace Nova.ViewModels.Pages.Audio
{
    /// <summary>
    /// The Setting page's Manual tab: the manual-operation GUI (the Manual screens' device
    /// tabs, and its Log tab with the Homing and reset buttons, the running log and the
    /// alarms). The buttons' commands are here; the Log tab shows them.
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

        public ManualTabViewModel(
            PageViewModel gui = null,
            IHomingService homing = null,
            IMachineOrchestrator orchestrator = null,
            IDialogService dialog = null,
            IAppSession session = null,
            IAccessLog accessLog = null)
        {
            Gui = gui;
            _homing = homing;

            // The Homing and reset buttons are drawn on the GUI's Log tab.
            var manualGui = gui as ManualViewModel;
            if (manualGui != null)
                manualGui.Log.MachineControls = this;
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
        // Homing button (top of the Log tab)
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
            // FAM 2 Bug_Id_006: homing in an E-stop or with an alarm standing was accepted,
            // and its "homing done" came after the E-stop was released. Refuse it here, in
            // the words QA asked for; the PLC still has the last word.
            if (_orchestrator != null && _orchestrator.CurrentStatus == MachineStatus.Alarm)
            {
                _dialog?.ShowWarning("Machine is in alarm state. Clear the alarm (Reset) before homing.", "Homing");
                return;
            }

            if (_orchestrator != null && _orchestrator.IsAutoRunning)
            {
                _dialog?.ShowWarning("Homing cannot be started while the auto sequence is running.", "Homing");
                return;
            }

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

        // ==================================================================
        // Reset button (the small unlabelled one beside Homing, on the Log tab)
        // ==================================================================

        /// <summary>Whether there is a machine orchestrator behind the button.</summary>
        public bool HasReset => _orchestrator != null;

        /// <summary>Pulses the PLC's RESET: the PLC acknowledges its alarms and leaves ALARM / STOP.</summary>
        [RelayCommand]
        private void ResetMachine()
        {
            try
            {
                _orchestrator.ResetAlarms();
                LogAction("Reset pressed");
            }
            catch (Exception ex)
            {
                _dialog?.ShowError("The PLC could not be reset: " + ex.Message, "Manual");
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
        }
    }
}
