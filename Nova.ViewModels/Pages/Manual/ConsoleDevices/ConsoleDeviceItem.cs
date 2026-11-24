using System.Collections.ObjectModel;

namespace Nova.ViewModels.Pages.Manual
{
    /// <summary>
    /// A selectable entry for a console-style device tab (Scanner /
    /// SFC &amp; PDCA).
    ///
    /// <see cref="Payload"/> is the device's default command as configured. It is NOT
    /// loaded into the Send Command editor — that box is left for the operator to fill
    /// (see <c>ConsoleDeviceViewModel.OnDeviceSelected</c>). Kept because it still
    /// documents what a device expects, and the comm-device profile supplies it.
    /// </summary>
    public class ConsoleDeviceItem
    {
        public string Name { get; }
        public string Detail { get; }
        public string Payload { get; }

        /// <summary>"Name - Detail" label for the selected-device card.</summary>
        public string DisplayName => string.IsNullOrEmpty(Detail) ? Name : Name + " - " + Detail;

        /// <summary>
        /// This device's own Received Response history. FAM 2 Bug_Id_038 / MS_11: one shared
        /// console showed every device's answers together, and kept them when the device changed.
        /// </summary>
        public ObservableCollection<CameraConsoleLine> Console { get; } = new ObservableCollection<CameraConsoleLine>();

        public ConsoleDeviceItem(string name, string detail = "", string payload = "")
        {
            Name = name;
            Detail = detail;
            Payload = payload;
        }
    }
}
