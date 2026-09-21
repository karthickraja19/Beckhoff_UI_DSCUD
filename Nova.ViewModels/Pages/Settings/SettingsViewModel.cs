using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Nova.Abstractions.Data;
using Nova.Abstractions.Data.Models;
using Nova.Abstractions.Services;
using Nova.ViewModels.Common;

namespace Nova.ViewModels.Pages.Settings
{
    /// <summary>
    /// The machine's configuration tree, hosted in the Config page's "Machine Configs"
    /// area.
    /// </summary>
    /// <remarks>
    /// Audio SW DFM v5.4 slide 58 leaves the 2/3-width region labelled "Machine Configs"
    /// undefined, because what a machine has to be configured with is per-machine. For
    /// this machine it is these tabs. Access follows the same slide: Level 1 view only,
    /// Level 2 non-critical config, Level 3 everything - enforced by the host page, which
    /// knows the operator's level.
    /// </remarks>
    public partial class SettingsViewModel : PageViewModel, ITabHost
    {
        // Not AppPageNames.Settings: that key belongs to the DFM's own Settings page
        // (slides 62-64, station identity and hardware type), which AudioSettingsViewModel
        // serves. This is the machine's configuration tree, hosted inside Config's
        // "Machine Configs" area (slide 58).
        public override AppPageNames PageName => AppPageNames.MachineConfig;

        public ObservableCollection<TabItemViewModel> Tabs { get; }

        [ObservableProperty]
        private TabItemViewModel _selectedTab;

        /// <summary>Switching tabs abandons an unsaved edit on the one being left,
        /// nested sub-tabs included.</summary>
        partial void OnSelectedTabChanged(TabItemViewModel oldValue, TabItemViewModel newValue)
        {
            oldValue?.CancelEdits();
        }

        /// <summary>
        /// Leaving the page does the same. This view model is a DI singleton, so
        /// without it a grid left unlocked was still unlocked — with whatever was typed
        /// into it — when the operator navigated back.
        /// </summary>
        public override void OnNavigatedFrom()
        {
            foreach (var tab in Tabs)
            {
                tab?.CancelEdits();
            }

            base.OnNavigatedFrom();
        }

        public SettingsViewModel(
            IDialogService dialogService,
            IMachineSettingsService machineSettingsService,
            IDigitalIOService digitalIoService,
            ILoadCellMonitorService loadCellMonitorService,
            IMachineContext machineContext,
            // Needed by Device Settings: a mode change stops the sequence first.
            Nova.Abstractions.Machine.Interfaces.IMachineOrchestrator orchestrator,
            Nova.Abstractions.Data.Repositories.IShiftSettingsRepository shiftSettings = null,
            // SFC & PDCA: the AE-limit tabs export their table to CSV.
            ICsvExportService csv = null)
        {
            if (dialogService == null) throw new System.ArgumentNullException(nameof(dialogService));
            if (machineSettingsService == null) throw new System.ArgumentNullException(nameof(machineSettingsService));
            if (digitalIoService == null) throw new System.ArgumentNullException(nameof(digitalIoService));
            if (loadCellMonitorService == null) throw new System.ArgumentNullException(nameof(loadCellMonitorService));
            if (machineContext == null) throw new System.ArgumentNullException(nameof(machineContext));
            if (orchestrator == null) throw new System.ArgumentNullException(nameof(orchestrator));

            var axisParams = new AxisConfigurationViewModel(machineSettingsService, dialogService);

            var ioGroup = new SettingsGroupViewModel("I/O Diagnostics",
                new TabItemViewModel("Digital I/O", "ToggleRight", new DigitalIoViewModel(digitalIoService)),
                new TabItemViewModel("Analog I/O", "Gauge", new AnalogIoViewModel(loadCellMonitorService)));

            var sfcGroup = new SettingsGroupViewModel("SFC & PDCA Settings",
                new TabItemViewModel("AE limits (Production)", "", new AeLimitsViewModel("AE limits (Production)", AeLimitKind.Production, machineSettingsService, dialogService, csv)),
                new TabItemViewModel("AE limits (CPK)", "", new AeLimitsViewModel("AE limits (CPK)", AeLimitKind.Cpk, machineSettingsService, dialogService, csv)),
                new TabItemViewModel("AE limits (GRR)", "", new AeLimitsViewModel("AE limits (GRR)", AeLimitKind.Grr, machineSettingsService, dialogService, csv)));

            var calibrationGroup = new SettingsGroupViewModel("Calibration",
                new TabItemViewModel("Camera", "Camera", new CalibrationCameraViewModel()),
                new TabItemViewModel("Robot", "Bot", new CalibrationRobotViewModel()));

            // Top-level tabs mirror the V3 Settings form's tabControlSettings; the grouped
            // ones (Device / I/O / SFC / Calibration) host nested tabs like the original.
            Tabs = new ObservableCollection<TabItemViewModel>
            {
                new TabItemViewModel("General", "Settings2", new GeneralSettingsViewModel(dialogService, shiftSettings)),

                new TabItemViewModel("Device Settings", "Cpu", new DeviceSettingsViewModel(machineContext, orchestrator, dialogService)),

                new TabItemViewModel("I/O Diagnostics", "Activity", ioGroup),

                new TabItemViewModel("Axis Configuration Settings", "Move", axisParams),

                new TabItemViewModel("Vision Settings", "Eye", new VisionSettingsViewModel(machineSettingsService, dialogService)),

                new TabItemViewModel("SFC & PDCA Settings", "ClipboardList", sfcGroup),

                new TabItemViewModel("Calibration", "Crosshair", calibrationGroup),
            };

            SelectedTab = Tabs[0];
        }
    }
}
