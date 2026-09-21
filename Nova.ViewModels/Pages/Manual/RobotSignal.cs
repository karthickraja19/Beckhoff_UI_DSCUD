using CommunityToolkit.Mvvm.ComponentModel;

namespace Nova.ViewModels.Pages.Manual
{
    /// <summary>
    /// One robot digital I/O point. Digital inputs render <see cref="IsOn"/> as a
    /// read-only indicator; digital outputs bind it two-way to a toggle switch.
    /// </summary>
    public partial class RobotSignal : ObservableObject
    {
        public string Name { get; }

        [ObservableProperty]
        private bool _isOn;

        public RobotSignal(string name, bool isOn = false)
        {
            Name = name;
            _isOn = isOn;
        }
    }
}
