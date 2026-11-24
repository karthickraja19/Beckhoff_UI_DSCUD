using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Nova.Abstractions.Data;
using Nova.Abstractions.Services;
using Nova.ViewModels.Common;

namespace Nova.ViewModels.Pages.Manual
{
    /// <summary>
    /// The manual-operation GUI, hosted in the Setting page's Manual tab.
    /// </summary>
    /// <remarks>
    /// Audio SW DFM v5.4 slide 57 gives the GUI for manual operation as the Home page's
    /// Vendor tab; this HMI keeps it on the Setting page instead and leaves the Vendor tab
    /// empty. It is not a routable page of its own -
    /// <see cref="Nova.ViewModels.Pages.Audio.ManualTabViewModel"/> hosts it.
    /// </remarks>
    public partial class ManualViewModel : PageViewModel, ITabHost
    {
        public override AppPageNames PageName => AppPageNames.Manual;

        public ObservableCollection<TabItemViewModel> Tabs { get; }

        /// <summary>The Log tab: Homing, reset, the running log and the alarms.</summary>
        public ManualLogViewModel Log { get; }

        [ObservableProperty]
        private TabItemViewModel _selectedTab;

        /// <summary>True while the manual GUI is on screen - the Setting page with its Manual tab in front.</summary>
        private bool _isShown;

        /// <summary>Only the tab in front follows the live status of its devices.</summary>
        partial void OnSelectedTabChanged(TabItemViewModel oldValue, TabItemViewModel newValue)
        {
            if (!_isShown)
                return;

            oldValue?.Deactivate();
            newValue?.Activate();
        }

        /// <summary>Called by the Setting page when the Manual tab comes into view.</summary>
        public override void OnNavigatedTo()
        {
            base.OnNavigatedTo();

            if (_isShown)
                return;

            _isShown = true;
            SelectedTab?.Activate();
        }

        /// <summary>Called by the Setting page when the Manual tab, or the page, is left.</summary>
        public override void OnNavigatedFrom()
        {
            if (_isShown)
            {
                _isShown = false;
                SelectedTab?.Deactivate();
            }

            base.OnNavigatedFrom();
        }

        public ManualViewModel(IAxisService axisService, IAxisMonitorService axisMonitor,
            IStepperService stepperService, IDigitalIOService digitalIO,
            ICylinderService cylinderService, IVacuumService vacuumService,
            ICommDeviceService commService,
            // Manual motion refuses out-of-range speeds out loud, as V3 does, so the
            // Axis and Stepper tabs need somewhere to say so.
            IDialogService dialogService = null,
            // Supplies the Axis tab's read-only parameter panel from Servos.json.
            IMachineSettingsService settingsService = null,
            // Lets the TCP/IP device tabs show a device that is already connected.
            IConnectionStatusService connectionStatus = null,
            // The robots' EtherCAT I/O for the Robot tab.
            IRobotIOService robotIO = null,
            // The Log tab: the running log, and the PLC's alarms standing now.
            IRunningLogService runningLog = null,
            IActiveAlarmService activeAlarms = null,
            // The dispensers, weighing scale and vibration sensors the PLC drives over serial links.
            IThirdPartyDevices thirdPartyDevices = null)
        {
            var log = new ManualLogViewModel(runningLog, activeAlarms);
            Log = log;
            var logTab = new TabItemViewModel("Log", "FileText", log, "Manual.Log");

            // The Log tab's header is marked while an alarm stands, seen from any tab.
            logTab.HasAlert = log.HasAlarms;
            log.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(ManualLogViewModel.HasAlarms))
                    logTab.HasAlert = log.HasAlarms;
            };

            // Icons mirror the sidebar's Manual entries (Lucide kinds). Order is the
            // one QA signed off on: Axis, IO Stepper, Cylinder, Vacuum, Scanner, CCD,
            // SFC & PDCA, Robot. The keys are navigation identifiers and
            // deliberately keep their original values ("Manual.Stepper", "Manual.Camera")
            // even though the visible titles changed — they pair with the sidebar's
            // NavTreeItem keys, and renaming them would break navigation for a label change.
            // Log comes last: it is not a device.
            Tabs = new ObservableCollection<TabItemViewModel>
            {
                new TabItemViewModel("Axis", "Move", new AxisControlViewModel(axisService, axisMonitor, dialogService, settingsService), "Manual.Axis"),
                new TabItemViewModel("IO Stepper", "Activity", new StepperControlViewModel(stepperService, axisMonitor, dialogService), "Manual.Stepper"),
                new TabItemViewModel("Cylinder", "CircleDot", new CylinderControlViewModel(cylinderService, digitalIO), "Manual.Cylinder"),
                new TabItemViewModel("Vacuum", "Wind", new VacuumControlViewModel(vacuumService, digitalIO), "Manual.Vacuum"),
                new TabItemViewModel("Scanner", "ScanLine", new ScannerControlViewModel(commService, connectionStatus), "Manual.Scanner"),
                new TabItemViewModel("CCD", "Camera", new CameraControlViewModel(commService, connectionStatus), "Manual.Camera"),
                new TabItemViewModel("SFC & PDCA", "ClipboardList", new SfcPdcaControlViewModel(commService, connectionStatus), "Manual.SFC"),
                new TabItemViewModel("Robot", "Bot", new RobotControlViewModel(robotIO), "Manual.Robot"),
                new TabItemViewModel("Dispenser", "Droplets",
                    new DispenserControlViewModel(thirdPartyDevices?.Dispensers, dialogService), "Manual.Dispenser"),
                new TabItemViewModel("Weighing", "Scale",
                    new WeighingScaleControlViewModel(thirdPartyDevices?.WeighingScales, dialogService), "Manual.Weighing"),
                new TabItemViewModel("Vibration", "AudioWaveform",
                    new VibrationSensorControlViewModel(thirdPartyDevices?.VibrationSensors, dialogService), "Manual.Vibration"),
                logTab
            };

            SelectedTab = Tabs[0];
        }
    }
}
