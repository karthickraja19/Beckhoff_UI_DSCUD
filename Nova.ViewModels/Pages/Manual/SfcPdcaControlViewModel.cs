using Nova.Abstractions.Services;

namespace Nova.ViewModels.Pages.Manual
{
    /// <summary>
    /// "SFC &amp; PDCA" tab — the SFC/PDCA server link (V3: ObjectList.SFC connect /
    /// trigger / disconnect + connection status). Servers come from
    /// <see cref="ICommDeviceService"/>; Trigger = SEND (posts the PDCA record).
    /// </summary>
    public class SfcPdcaControlViewModel : ConsoleDeviceViewModel
    {
        public override string SelectorCaption => "Select SFC Server";
        public override string DeviceCaption => "SELECTED SERVER";
        public override string TriggerLabel => "SEND";
        public override string TriggerIcon => "Send";

        public SfcPdcaControlViewModel(ICommDeviceService comm, IConnectionStatusService connectionStatus = null)
            : base(connectionStatus)
        {
            LoadDevices(comm?.GetDevices(CommDeviceKind.Sfc));

            if (!HasTransports)
                Console.Add(new CameraConsoleLine("No SFC server is available: there is no SFC client in this build yet."));
        }
    }
}
