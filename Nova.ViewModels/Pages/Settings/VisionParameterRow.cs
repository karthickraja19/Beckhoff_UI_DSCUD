using CommunityToolkit.Mvvm.ComponentModel;

namespace Nova.ViewModels.Pages.Settings
{
    /// <summary>
    /// One vision colour parameter (V3 Table_Vision_Settings row: Colour / OPT_ID /
    /// Camera_ID). <see cref="IsActive"/> marks the recipe currently applied.
    /// </summary>
    public partial class VisionParameterRow : ObservableObject
    {
        [ObservableProperty]
        private string _colour;

        [ObservableProperty]
        private string _optId;

        [ObservableProperty]
        private string _cameraId;

        [ObservableProperty]
        private bool _isActive;

        public VisionParameterRow(string colour, string optId, string cameraId, bool isActive = false)
        {
            _colour = colour;
            _optId = optId;
            _cameraId = cameraId;
            _isActive = isActive;
        }
    }
}
