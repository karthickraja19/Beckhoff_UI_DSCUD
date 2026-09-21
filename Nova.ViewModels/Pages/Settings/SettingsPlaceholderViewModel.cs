using CommunityToolkit.Mvvm.ComponentModel;

namespace Nova.ViewModels.Pages.Settings
{
    /// <summary>
    /// Stand-in for a Settings tab whose editor has not been built out yet. Each
    /// Settings tab starts here and is replaced by a real ViewModel/View as it is
    /// implemented (same scaffolding approach used by the Manual screen).
    /// </summary>
    public partial class SettingsPlaceholderViewModel : ObservableObject
    {
        public string Title { get; }

        public SettingsPlaceholderViewModel(string title)
        {
            Title = title;
        }
    }
}
