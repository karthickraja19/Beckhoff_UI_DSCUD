using System.Collections.ObjectModel;

namespace Nova.ViewModels.Pages.Settings
{
    /// <summary>A titled card of related <see cref="SettingsField"/> rows.</summary>
    public class SettingsSection
    {
        public string Title { get; }
        public string Icon { get; }
        public ObservableCollection<SettingsField> Fields { get; }

        public SettingsSection(string title, string icon, params SettingsField[] fields)
        {
            Title = title;
            Icon = icon;
            Fields = new ObservableCollection<SettingsField>(fields);
        }
    }
}
