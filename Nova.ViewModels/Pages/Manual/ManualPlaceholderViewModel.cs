using CommunityToolkit.Mvvm.ComponentModel;

namespace Nova.ViewModels.Pages.Manual
{
    /// <summary>
    /// Lightweight stand-in for Manual tabs that have not been implemented yet
    /// (Stepper, Cylinder, Vacuum, ...). Only the Axis tab is currently built out.
    /// </summary>
    public partial class ManualPlaceholderViewModel : ObservableObject
    {
        public string Title { get; }

        public ManualPlaceholderViewModel(string title)
        {
            Title = title;
        }
    }
}
