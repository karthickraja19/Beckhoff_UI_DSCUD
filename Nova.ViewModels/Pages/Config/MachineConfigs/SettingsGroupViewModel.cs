using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Nova.ViewModels.Common;

namespace Nova.ViewModels.Pages.Settings
{
    /// <summary>
    /// A top-level Settings tab that itself hosts nested tabs (mirrors the V3 form,
    /// where e.g. "Device Settings" and "Calibration" contain their own tab controls).
    /// </summary>
    public partial class SettingsGroupViewModel : ObservableObject, ITabHost
    {
        public string Title { get; }
        public ObservableCollection<TabItemViewModel> Tabs { get; }

        [ObservableProperty]
        private TabItemViewModel _selectedTab;

        public SettingsGroupViewModel(string title, params TabItemViewModel[] tabs)
        {
            Title = title;
            Tabs = new ObservableCollection<TabItemViewModel>(tabs);
            _selectedTab = Tabs.Count > 0 ? Tabs[0] : null;
        }

        /// <summary>Switching sub-tabs abandons an unsaved edit on the one being left.</summary>
        partial void OnSelectedTabChanged(TabItemViewModel oldValue, TabItemViewModel newValue)
        {
            oldValue?.CancelEdits();
        }
    }
}
