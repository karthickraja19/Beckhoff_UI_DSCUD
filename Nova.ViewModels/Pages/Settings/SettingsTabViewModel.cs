using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Nova.ViewModels.Pages.Settings
{
    /// <summary>
    /// Generic ViewModel for a Settings tab: a set of <see cref="SettingsSection"/>
    /// cards rendered by the shared settings form. Fields are locked (view mode) until
    /// <see cref="EditCommand"/> is invoked; <see cref="SaveCommand"/> commits and re-locks.
    /// </summary>
    public partial class SettingsTabViewModel : ObservableObject
    {
        public string Title { get; }
        public ObservableCollection<SettingsSection> Sections { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanEdit))]
        private bool _isEditing;

        public bool CanEdit => !IsEditing;

        public SettingsTabViewModel(string title, params SettingsSection[] sections)
        {
            Title = title;
            Sections = new ObservableCollection<SettingsSection>(sections);
        }

        [RelayCommand]
        private void Edit() => IsEditing = true;

        [RelayCommand]
        private void Save() => IsEditing = false;
        // TODO: persist Sections/Fields to configuration on Save.
    }
}
