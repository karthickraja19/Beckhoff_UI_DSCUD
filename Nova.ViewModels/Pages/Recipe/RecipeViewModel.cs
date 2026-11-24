using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Nova.Abstractions.Data;
using Nova.Abstractions.Machine.Interfaces;
using Nova.Abstractions.Services;
using Nova.ViewModels.Common;

namespace Nova.ViewModels.Pages.Recipe
{
    /// <summary>
    /// Taught gantry positions, robot values and CNC positions, hosted in the Config
    /// page's "Machine Configs" area.
    /// </summary>
    /// <remarks>
    /// Audio SW DFM v5.4 does not mention recipes anywhere - it is a concept this
    /// machine has and the spec does not model. It sits under Config rather than
    /// anywhere else because taught positions are commissioned per machine, which is
    /// what slide 58's "Machine Configs" region is for. Every tab here edits a taught
    /// position or a value written to the PLC, so all of it is critical: the host page gates this
    /// section at Level 3, not Level 2.
    /// </remarks>
    public partial class RecipeViewModel : PageViewModel, ITabHost
    {
        public override AppPageNames PageName => AppPageNames.Recipe;

        public ObservableCollection<TabItemViewModel> Tabs { get; }

        [ObservableProperty]
        private TabItemViewModel _selectedTab;

        /// <summary>True while this section is on screen - the Config page with Recipe in front.</summary>
        private bool _isShown;

        /// <summary>
        /// Switching tabs abandons an unsaved edit on the one being left. Only the tab in
        /// front follows live status (the Gantry tab's axes).
        /// </summary>
        partial void OnSelectedTabChanged(TabItemViewModel oldValue, TabItemViewModel newValue)
        {
            // A refused tab was put back: nothing changed.
            if (_revertingTab)
                return;

            // The Gantry and CNC tabs hold manual mode, which the PLC does not allow while
            // the auto sequence is running.
            if (HoldsManual(newValue) && IsAutoRunning)
            {
                _revertingTab = true;
                try
                {
                    SelectedTab = oldValue ?? FirstTabWithoutManual();
                }
                finally
                {
                    _revertingTab = false;
                }

                _dialogService.ShowWarning(ManualBlockedMessage, "Recipe");
                return;
            }

            oldValue?.CancelEdits();

            if (_isShown)
            {
                oldValue?.Deactivate();
                newValue?.Activate();
            }
        }

        /// <summary>Called by the Config page when the Recipe section comes into view.</summary>
        public override void OnNavigatedTo()
        {
            base.OnNavigatedTo();

            if (_isShown)
                return;

            // The machine started while the operator was elsewhere: do not go back into a
            // tab that needs manual mode.
            if (HoldsManual(SelectedTab) && IsAutoRunning)
            {
                SelectedTab = FirstTabWithoutManual();
                _dialogService.ShowWarning(ManualBlockedMessage, "Recipe");
            }

            _isShown = true;
            SelectedTab?.Activate();
        }

        private const string ManualBlockedMessage =
            "The Gantry and CNC tabs cannot be opened while the auto sequence is running. Stop the machine first.";

        private readonly IDialogService _dialogService;
        private readonly IMachineOrchestrator _orchestrator;
        private bool _revertingTab;

        private bool IsAutoRunning => _orchestrator != null && _orchestrator.IsAutoRunning;

        /// <summary>The Gantry and CNC tabs take manual mode while on screen; the Robot tab does not.</summary>
        private static bool HoldsManual(TabItemViewModel tab)
        {
            return tab != null && (tab.ViewModel is GantryRecipeViewModel || tab.ViewModel is CNCRecipeViewModel);
        }

        private TabItemViewModel FirstTabWithoutManual()
        {
            foreach (TabItemViewModel tab in Tabs)
            {
                if (!HoldsManual(tab))
                    return tab;
            }

            return Tabs[0];
        }

        /// <summary>The auto sequence started while a manual-mode tab was open: leave it, which releases manual mode.</summary>
        private void OnMachineStatusChanged(Nova.Abstractions.Machine.Enums.MachineStatus status)
        {
            if (_isShown && HoldsManual(SelectedTab) && IsAutoRunning)
                SelectedTab = FirstTabWithoutManual();
        }

        /// <summary>
        /// Leaving the page abandons unsaved edits too. This view model is a DI singleton,
        /// so without it the Gantry grid stayed unlocked — with whatever was typed into
        /// it — when the operator navigated back.
        /// </summary>
        public override void OnNavigatedFrom()
        {
            foreach (var tab in Tabs)
            {
                tab?.CancelEdits();
            }

            if (_isShown)
            {
                _isShown = false;
                SelectedTab?.Deactivate();
            }

            base.OnNavigatedFrom();
        }

        public RecipeViewModel(
            IRecipeService recipeService,
            IDialogService dialogService,
            IAxisService axisService,
            IAxisMonitorService axisMonitor,
            ILoadCellMonitorService loadCellMonitor,
            IPlcRecipeService plcRecipeService,
            IMachineOrchestrator orchestrator,
            // The CNC tab: the axes of the CNC channels, jogged through their channel.
            ICncAxisProvider cncAxes = null,
            // The CNC tab's button that starts Beckhoff's CNC HMI.
            ICncHmiLauncher cncHmi = null)
        {
            if (recipeService == null) throw new System.ArgumentNullException(nameof(recipeService));
            if (dialogService == null) throw new System.ArgumentNullException(nameof(dialogService));
            if (axisService == null) throw new System.ArgumentNullException(nameof(axisService));
            if (axisMonitor == null) throw new System.ArgumentNullException(nameof(axisMonitor));
            if (loadCellMonitor == null) throw new System.ArgumentNullException(nameof(loadCellMonitor));
            if (plcRecipeService == null) throw new System.ArgumentNullException(nameof(plcRecipeService));
            if (orchestrator == null) throw new System.ArgumentNullException(nameof(orchestrator));

            _dialogService = dialogService;
            _orchestrator = orchestrator;
            _orchestrator.OnMachineStatusChanged += OnMachineStatusChanged;

            // Icons are Lucide kinds, matching the Manual screen's tab strip.
            Tabs = new ObservableCollection<TabItemViewModel>
            {
                new TabItemViewModel("Gantry", "LayoutGrid", new GantryRecipeViewModel(recipeService, dialogService, axisService, axisMonitor, loadCellMonitor, plcRecipeService, orchestrator)),
                new TabItemViewModel("Robot", "Bot", new RobotRecipeViewModel(recipeService, dialogService, plcRecipeService)),
                new TabItemViewModel("CNC", "Drill", new CNCRecipeViewModel(recipeService, dialogService, axisService, axisMonitor, plcRecipeService, orchestrator, cncAxes, cncHmi)),
            };

            SelectedTab = Tabs[0];
        }
    }
}
