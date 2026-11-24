using System.Collections.ObjectModel;
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

        /// <summary>This CCD's own Received Response history (FAM 2 Bug_Id_038 / MS_16).</summary>
        public ObservableCollection<CameraConsoleLine> Console { get; } = new ObservableCollection<CameraConsoleLine>();

        /// <summary>
        /// This CCD's Send Command, kept while another CCD is selected. FAM 2 MS_15: one box
        /// was shared, so an edit made for one CCD was what the next one was sent.
        /// </summary>
        public string SendCommand { get; set; }

        public CcdDeviceViewModel(string name, string model, INetworkProtocol protocol = null, string defaultCommand = null)
        {
            Name = name;
            Model = model;
            Protocol = protocol;
            SendCommand = defaultCommand ?? string.Empty;
        }
    }
}
