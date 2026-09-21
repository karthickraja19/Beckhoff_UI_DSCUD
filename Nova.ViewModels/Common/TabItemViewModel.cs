using CommunityToolkit.Mvvm.ComponentModel;

namespace Nova.ViewModels.Common
{
    public partial class TabItemViewModel : ObservableObject
    {
        public string Title { get; }
        public string Icon { get; }
        public object ViewModel { get; }

        /// <summary>
        /// Stable navigation key, matching the sidebar's NavTreeItem key for this tab
        /// (for example "Manual.Axis"). Sidebar navigation resolves a tab by this value
        /// rather than by its position, so reordering or renaming tabs cannot silently
        /// send a sidebar click to the wrong screen. Null for tabs the sidebar does not
        /// address directly.
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// Draws attention to this tab while something on it needs the operator — used by
        /// the Home screen's Alarm tab so a standing alarm is visible from whichever tab
        /// the operator happens to be on. False for every tab that never sets it, so the
        /// shared header template is unaffected elsewhere.
        /// </summary>
        [ObservableProperty]
        private bool _hasAlert;

        public TabItemViewModel(string title, string icon, object viewModel, string key = null)
        {
            Title = title;
            Icon = icon;
            ViewModel = viewModel;
            Key = key;
        }

        /// <summary>
        /// Ends an unsaved edit on the tab being left, and on any tab nested inside it.
        /// Page view models are DI singletons, so without this a grid left unlocked
        /// stayed unlocked — with half-typed cells — when the operator came back.
        /// Recurses because Settings nests tab groups (SFC &amp; PDCA, Axis Parameter
        /// Settings) whose children are the editable ones.
        /// </summary>
        public void CancelEdits()
        {
            CancelEdits(ViewModel);
        }

        internal static void CancelEdits(object viewModel)
        {
            var editable = viewModel as IEditableTab;
            if (editable != null)
            {
                editable.CancelEdit();
            }

            var host = viewModel as ITabHost;
            if (host?.Tabs == null)
            {
                return;
            }

            foreach (var tab in host.Tabs)
            {
                tab?.CancelEdits();
            }
        }
    }

    /// <summary>
    /// A view model that hosts tabs. Lets <see cref="TabItemViewModel.CancelEdits"/>
    /// walk into nested tab groups without knowing their concrete types.
    /// </summary>
    public interface ITabHost
    {
        System.Collections.ObjectModel.ObservableCollection<TabItemViewModel> Tabs { get; }
    }
}
