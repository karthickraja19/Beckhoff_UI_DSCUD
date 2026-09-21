using CommunityToolkit.Mvvm.ComponentModel;

namespace Nova.ViewModels.Pages.Manual
{
    /// <summary>
    /// A single live status flag for the selected axis (e.g. "Axis enable", "Positive limit").
    /// Rendered as a tinted status chip in the Device Status card: green when an ordinary
    /// flag is active, red when an alarm-type flag (error / limit) is active, grey otherwise.
    /// </summary>
    public partial class AxisStatusItem : ObservableObject
    {
        public string Label { get; }

        /// <summary>True for flags that signal a fault when active (error, limit switches).</summary>
        public bool IsAlarm { get; }

        [ObservableProperty]
        private bool _isActive;

        public AxisStatusItem(string label, bool isActive = false, bool isAlarm = false)
        {
            Label = label;
            _isActive = isActive;
            IsAlarm = isAlarm;
        }
    }
}
