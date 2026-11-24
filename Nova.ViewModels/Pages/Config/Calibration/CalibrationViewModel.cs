using System;
using System.Collections.ObjectModel;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Nova.Abstractions.Data;
using Nova.Abstractions.Machine.Enums;
using Nova.Abstractions.Machine.Interfaces;
using Nova.Abstractions.Services;
using Nova.ViewModels.Common;

namespace Nova.ViewModels.Pages.Calibration
{
    /// <summary>
    /// The Config page's Calibration section: one tab per table of taught positions (9 point,
    /// 5 axis translation, 5 axis rotation, camera needle, needle module), then the
    /// Camera and Robot tabs (V3's calibration screens) with the devices' connection state.
    /// </summary>
    /// <remarks>
    /// The PLC runs a calibration only in MANUAL or AUTO_RUNNING. Manual mode is a switch
    /// here rather than held while the section is on screen: letting go of it sends the PLC
    /// to IDLE, which needs a homing, so it is only taken when the operator asks for it. It
    /// is let go when the section is left.
    /// </remarks>
    public partial class CalibrationViewModel : PageViewModel, ITabHost
    {
        public override AppPageNames PageName => AppPageNames.Calibration;

        private readonly IDialogService _dialog;
        private readonly IMachineOrchestrator _orchestrator;
        private readonly SynchronizationContext _ui = SynchronizationContext.Current;

        private bool _isShown;
        private bool _isManualHeld;

        public ObservableCollection<TabItemViewModel> Tabs { get; }

        [ObservableProperty]
        private TabItemViewModel _selectedTab;

        public CalibrationViewModel(
            ICalibrationService calibration,
            IDialogService dialog,
            IMachineOrchestrator orchestrator = null,
            IConnectionStatusService connections = null,
            IRobotIOService robots = null,
            IStaticDynamicService staticDynamic = null)
        {
            if (calibration == null) throw new ArgumentNullException(nameof(calibration));
            _dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));
            _orchestrator = orchestrator;

            if (_orchestrator != null)
                _orchestrator.OnMachineStatusChanged += OnMachineStatusChanged;

            Tabs = new ObservableCollection<TabItemViewModel>();
            foreach (CalibrationTable table in new CalibrationTable[]
                     {
                         new NinePointTable(), null, new CameraNeedleTable(), new NeedleModuleTable()
                     })
            {
                if (table == null)
                {
                    // 5 axis: translation and rotation on one tab, one Start / Stop for the one sequence.
                    var fiveAxis = new FiveAxisViewModel(calibration, dialog);
                    Tabs.Add(new TabItemViewModel(fiveAxis.Title, fiveAxis.Icon, fiveAxis));
                    continue;
                }

                Tabs.Add(new TabItemViewModel(table.Title, table.Icon, new CalibrationTableViewModel(table, calibration, dialog)));
            }

            // Static & Dynamic: the CCD verification the PLC's Static_Seq / Dynamic_Seq run. It needs the
            // machine in AUTO, so unlike the tables it is not a manual-mode job.
            if (staticDynamic != null)
            {
                var staticDynamicTab = new StaticDynamicViewModel(staticDynamic, dialog, orchestrator);
                Tabs.Add(new TabItemViewModel(staticDynamicTab.Title, staticDynamicTab.Icon, staticDynamicTab));
            }

            // V3's Camera and Robot calibration screens: the devices and whether they are connected.
            Tabs.Add(new TabItemViewModel("Camera", "Camera", new CalibrationDeviceViewModel(
                "Camera", "Camera", "CCD", "The CCD cameras the calibrations use, and whether each is connected.", connections)));
            Tabs.Add(new TabItemViewModel("Robot", "Bot", new CalibrationDeviceViewModel(
                "Robot", "Bot", "Robot", "The robots on this machine, and whether each is connected.", connections, robots)));

            SelectedTab = Tabs[0];
        }

        /// <summary>Switching tabs abandons an unsaved edit on the one being left.</summary>
        partial void OnSelectedTabChanged(TabItemViewModel oldValue, TabItemViewModel newValue)
        {
            oldValue?.CancelEdits();

            if (_isShown)
            {
                oldValue?.Deactivate();
                newValue?.Activate();
            }
        }

        /// <summary>Called by the Config page when the Calibration section comes into view.</summary>
        public override void OnNavigatedTo()
        {
            base.OnNavigatedTo();

            if (_isShown)
                return;

            _isShown = true;
            SelectedTab?.Activate();
        }

        /// <summary>Leaving the section abandons unsaved edits and lets manual mode go.</summary>
        public override void OnNavigatedFrom()
        {
            foreach (TabItemViewModel tab in Tabs)
                tab?.CancelEdits();

            if (_isShown)
            {
                _isShown = false;
                SelectedTab?.Deactivate();
            }

            IsManualHeld = false;

            base.OnNavigatedFrom();
        }


        // ==================================================================
        // Manual mode
        // ==================================================================

        /// <summary>True while this section holds the machine in manual mode.</summary>
        public bool IsManualHeld
        {
            get { return _isManualHeld; }
            set
            {
                if (_orchestrator == null || value == _isManualHeld)
                    return;

                if (value && _orchestrator.IsAutoRunning)
                {
                    _dialog.ShowWarning("Manual mode cannot be taken while the auto sequence is running. Stop the machine first.", "Calibration");
                    OnPropertyChanged();
                    return;
                }

                _isManualHeld = value;
                _orchestrator.SetManualMode(value);
                OnPropertyChanged();
            }
        }

        /// <summary>Taking manual mode is refused while the auto sequence runs; letting it go is always allowed.</summary>
        public bool CanHoldManual => _orchestrator != null && (_isManualHeld || !_orchestrator.IsAutoRunning);

        /// <summary>The PLC's machine state in words, beside the switch.</summary>
        public string MachineState => _orchestrator != null ? _orchestrator.CurrentStatus.ToString() : string.Empty;

        /// <summary>Raised on any thread.</summary>
        private void OnMachineStatusChanged(MachineStatus status)
        {
            Action refresh = () =>
            {
                OnPropertyChanged(nameof(CanHoldManual));
                OnPropertyChanged(nameof(MachineState));
            };

            if (_ui == null)
                refresh();
            else
                _ui.Post(_ => refresh(), null);
        }
    }
}
