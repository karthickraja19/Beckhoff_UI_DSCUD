using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Nova.ViewModels.Pages.Settings
{
    /// <summary>
    /// Calibration &gt; Camera — CCD calibration procedure (choose camera set, Start/Stop,
    /// live status). Mirrors the V3 grpCameraCalib.
    /// </summary>
    public partial class CalibrationCameraViewModel : ObservableObject
    {
        public string Title => "Camera";

        // Camera set radio (single choice): "CCD 1, 2, 3" vs "CCD 4".
        [ObservableProperty]
        private bool _isCcd123 = true;

        [ObservableProperty]
        private bool _isCcd4;

        [ObservableProperty]
        private string _statusText = "Ready for procedure";

        [ObservableProperty]
        private bool _isOnline = true;

        [RelayCommand]
        private void Start() => StatusText = "Calibration in progress…";   // TODO: begin CCD calibration

        [RelayCommand]
        private void Stop() => StatusText = "Ready for procedure";          // TODO: stop calibration
    }
}
