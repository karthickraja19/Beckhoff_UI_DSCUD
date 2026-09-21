using CommunityToolkit.Mvvm.ComponentModel;

namespace Nova.ViewModels.Pages.Manual
{
    /// <summary>
    /// One reed-switch input on a cylinder (Work or Home side): its digital-input
    /// address and live state. A cylinder side can carry several of these.
    /// </summary>
    public partial class CylinderSensor : ObservableObject
    {
        public string Address { get; }

        /// <summary>Density hint: true when the side carries several sensors,
        /// so the view renders this address chip smaller.</summary>
        public bool IsCompact { get; }

        [ObservableProperty]
        private bool _isOn;

        public CylinderSensor(string address, bool isOn = false, bool isCompact = false)
        {
            Address = address;
            IsCompact = isCompact;
            _isOn = isOn;
        }
    }
}
