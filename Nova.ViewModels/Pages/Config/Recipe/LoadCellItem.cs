using CommunityToolkit.Mvvm.ComponentModel;

namespace Nova.ViewModels.Pages.Recipe
{
    /// <summary>One loadcell readout shown on the Gantry recipe tab (value in kg).</summary>
    public partial class LoadCellItem : ObservableObject
    {
        public string Name { get; }

        [ObservableProperty]
        private double _value;

        public LoadCellItem(string name, double value = 0)
        {
            Name = name;
            _value = value;
        }
    }
}
