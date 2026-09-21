using Nova.Abstractions.Services;

namespace Nova.ViewModels.Pages.Manual
{
    /// <summary>
    /// "SFC &amp; PDCA" tab — the SFC/PDCA server link (V3: ObjectList.SFC connect /
    /// trigger / disconnect + connection status). Servers come from SFC.json via
    /// <see cref="ICommDeviceService"/>; Trigger = SEND (posts the PDCA record).
    /// </summary>
    public class SfcPdcaControlViewModel : ConsoleDeviceViewModel
    {
        public override string SelectorCaption => "Select SFC Server";
        public override string DeviceCaption => "SELECTED SERVER";
        public override string TriggerLabel => "SEND";
        public override string TriggerIcon => "Send";

        public SfcPdcaControlViewModel(ICommDeviceService comm)
        {
            LoadDevices(comm?.GetDevices(CommDeviceKind.Sfc));

            if (!HasTransports)
            {
                // Dev fallback (no SFC.json): keep the seeded demo console.
                Devices.Add(new ConsoleDeviceItem("SFC_Primary", "10.0.0.5:5000",
                    "PDCA_REPORT, STATION=BTH, RESULT=PASS, CT=12.5, END"));
                SelectedDevice = Devices[0];
                IsConnected = true;
                Console.Add(new CameraConsoleLine("SFC_HANDSHAKE..."));
                Console.Add(new CameraConsoleLine("ACK SESSION_OPEN", isAck: true));
            }
        }

        protected override void OnTrigger()
        {
            Console.Add(new CameraConsoleLine("SEND_PDCA"));
            Console.Add(new CameraConsoleLine("ACK RECORDED", isAck: true));
        }
    }
}
