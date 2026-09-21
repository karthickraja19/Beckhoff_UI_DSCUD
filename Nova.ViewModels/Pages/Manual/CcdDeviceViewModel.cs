using Nova.Abstractions.Hardware.Interfaces;

namespace Nova.ViewModels.Pages.Manual
{
    /// <summary>A selectable CCD/camera device: its logical name, sensor model, and TCP transport.</summary>
    public class CcdDeviceViewModel
    {
        public string Name { get; }
        public string Model { get; }

        /// <summary>The device's network transport (null in the seeded dev fallback).</summary>
        public INetworkProtocol Protocol { get; }

        /// <summary>"CCD1 - Sony IMX250" style label for the selected-device card.</summary>
        public string DisplayName => Model.Length > 0 ? Name + " - " + Model : Name;

        public CcdDeviceViewModel(string name, string model, INetworkProtocol protocol = null)
        {
            Name = name;
            Model = model;
            Protocol = protocol;
        }
    }
}
