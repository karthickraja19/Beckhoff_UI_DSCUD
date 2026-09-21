using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Nova.Abstractions.Data;
using Nova.Abstractions.Services;
using Nova.ViewModels.Common;

namespace Nova.ViewModels.Pages.Recipe
{
    /// <summary>
    /// Taught gantry positions, robot points and light settings, hosted in the Config
    /// page's "Machine Configs" area.
    /// </summary>
    /// <remarks>
    /// Audio SW DFM v5.4 does not mention recipes anywhere - it is a concept this
    /// machine has and the spec does not model. It sits under Config rather than
    /// anywhere else because taught positions are commissioned per machine, which is
    /// what slide 58's "Machine Configs" region is for. Every tab here edits a taught
    /// position or a light output, so all of it is critical: the host page gates this
    /// section at Level 3, not Level 2.
    /// </remarks>
    public partial class RecipeViewModel : PageViewModel, ITabHost
    {
        public override AppPageNames PageName => AppPageNames.Recipe;

        public ObservableCollection<TabItemViewModel> Tabs { get; }

        [ObservableProperty]
        private TabItemViewModel _selectedTab;

        /// <summary>Switching tabs abandons an unsaved edit on the one being left.</summary>
        partial void OnSelectedTabChanged(TabItemViewModel oldValue, TabItemViewModel newValue)
        {
            oldValue?.CancelEdits();
        }

        /// <summary>
        /// Leaving the page does the same. This view model is a DI singleton, so
        /// without it the Gantry grid stayed unlocked — with whatever was typed into
        /// it — when the operator navigated back.
        /// </summary>
        public override void OnNavigatedFrom()
        {
            foreach (var tab in Tabs)
            {
                tab?.CancelEdits();
            }

            base.OnNavigatedFrom();
        }

        public RecipeViewModel(
            IRecipeService recipeService,
            IDialogService dialogService,
            IAxisService axisService,
            IAxisMonitorService axisMonitor,
            ILoadCellMonitorService loadCellMonitor)
        {
            if (recipeService == null) throw new System.ArgumentNullException(nameof(recipeService));
            if (dialogService == null) throw new System.ArgumentNullException(nameof(dialogService));
            if (axisService == null) throw new System.ArgumentNullException(nameof(axisService));
            if (axisMonitor == null) throw new System.ArgumentNullException(nameof(axisMonitor));
            if (loadCellMonitor == null) throw new System.ArgumentNullException(nameof(loadCellMonitor));

            // Icons are Lucide kinds, matching the Manual screen's tab strip.
            Tabs = new ObservableCollection<TabItemViewModel>
            {
                new TabItemViewModel("Gantry", "LayoutGrid", new GantryRecipeViewModel(recipeService, dialogService, axisService, axisMonitor, loadCellMonitor)),
                new TabItemViewModel("Robot", "Bot", new RobotRecipeViewModel(recipeService, dialogService)),
                new TabItemViewModel("Light Control", "Lightbulb", new LightRecipeViewModel(recipeService, dialogService)),
            };

            SelectedTab = Tabs[0];
        }
    }
}
