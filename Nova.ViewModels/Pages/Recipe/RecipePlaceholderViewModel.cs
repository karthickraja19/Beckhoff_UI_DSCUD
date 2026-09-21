using CommunityToolkit.Mvvm.ComponentModel;

namespace Nova.ViewModels.Pages.Recipe
{
    /// <summary>
    /// Lightweight stand-in for Recipe tabs that have not been implemented yet
    /// (Robot, Light Control). Only the Gantry tab is currently built out.
    /// </summary>
    public partial class RecipePlaceholderViewModel : ObservableObject
    {
        public string Title { get; }

        public RecipePlaceholderViewModel(string title)
        {
            Title = title;
        }
    }
}
