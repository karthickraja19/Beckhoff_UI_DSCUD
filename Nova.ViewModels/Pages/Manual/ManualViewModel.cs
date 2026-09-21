using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Nova.Abstractions.Data;
using Nova.Abstractions.Services;
using Nova.ViewModels.Common;

namespace Nova.ViewModels.Pages.Manual
{
    /// <summary>
    /// The manual-operation GUI, hosted in the Home page's Vendor pane.
    /// </summary>
    /// <remarks>
    /// Audio SW DFM v5.4 slide 57 defines the Vendor tab for semi-auto stations that
    /// require operator loading or scanning, and gives its content as the GUI for
    /// manual operation. This is that GUI. It is no longer a routable page of its own -
    /// <see cref="Nova.ViewModels.Pages.Audio.AudioHomeViewModel"/> hosts it.
    /// </remarks>
    public partial class ManualViewModel : PageViewModel, ITabHost
    {
        public override AppPageNames PageName => AppPageNames.Manual;

        public ObservableCollection<TabItemViewModel> Tabs { get; }

        [ObservableProperty]
        private TabItemViewModel _selectedTab;

        public ManualViewModel(IAxisService axisService, IAxisMonitorService axisMonitor,
            IStepperService stepperService, IDigitalIOService digitalIO,
            ICylinderService cylinderService, IVacuumService vacuumService,
            ICommDeviceService commService, ILightService lightService,
            // Manual motion refuses out-of-range speeds out loud, as V3 does, so the
            // Axis and Stepper tabs need somewhere to say so.
            IDialogService dialogService = null,
            // Supplies the Axis tab's read-only parameter panel from Axis_Parameters.xml.
            IMachineSettingsService settingsService = null)
        {
            // Icons mirror the sidebar's Manual entries (Lucide kinds). Order is the
            // one QA signed off on: Axis, IO Stepper, Cylinder, Vacuum, Scanner, CCD,
            // SFC & PDCA, Robot, Light Control. The keys are navigation identifiers and
            // deliberately keep their original values ("Manual.Stepper", "Manual.Camera")
            // even though the visible titles changed — they pair with the sidebar's
            // NavTreeItem keys, and renaming them would break navigation for a label change.
            Tabs = new ObservableCollection<TabItemViewModel>
            {
                new TabItemViewModel("Axis", "Move", new AxisControlViewModel(axisService, axisMonitor, dialogService, settingsService), "Manual.Axis"),
                new TabItemViewModel("IO Stepper", "Activity", new StepperControlViewModel(stepperService, axisMonitor, dialogService), "Manual.Stepper"),
                new TabItemViewModel("Cylinder", "CircleDot", new CylinderControlViewModel(cylinderService, digitalIO), "Manual.Cylinder"),
                new TabItemViewModel("Vacuum", "Wind", new VacuumControlViewModel(vacuumService, digitalIO), "Manual.Vacuum"),
                new TabItemViewModel("Scanner", "ScanLine", new ScannerControlViewModel(commService), "Manual.Scanner"),
                new TabItemViewModel("CCD", "Camera", new CameraControlViewModel(commService), "Manual.Camera"),
                new TabItemViewModel("SFC & PDCA", "ClipboardList", new SfcPdcaControlViewModel(commService), "Manual.SFC"),
                new TabItemViewModel("Robot", "Bot", new RobotControlViewModel(commService, digitalIO), "Manual.Robot"),
                new TabItemViewModel("Light Control", "Lightbulb", new LightControlViewModel(lightService), "Manual.LightControl"),
            };

            SelectedTab = Tabs[0];
        }
    }
}
