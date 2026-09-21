using Nova.Abstractions.Services;

namespace Nova.ViewModels.Pages.Manual
{
    /// <summary>
    /// "Scanner" tab — a TCP barcode/2D scanner console (V3: cmbScanner + connect /
    /// trigger / disconnect + txtScannerSend / txtScannerReciv). Devices come from
    /// Scanners.json via <see cref="ICommDeviceService"/>; Connect/Trigger do real TCP.
    /// </summary>
    public class ScannerControlViewModel : ConsoleDeviceViewModel
    {
        public override string SelectorCaption => "Select Scanner";
        public override string TriggerLabel => "TRIGGER READ";
        public override string TriggerIcon => "ScanLine";

        public ScannerControlViewModel(ICommDeviceService comm)
        {
            LoadDevices(comm?.GetDevices(CommDeviceKind.Scanner));

            if (!HasTransports)
            {
                // Dev fallback (no config): keep the seeded demo console.
                Devices.Add(new ConsoleDeviceItem("Barcode_Scanner", "Cognex DM370", "SCAN_REQ, 1, MODE=AUTO, END"));
                Devices.Add(new ConsoleDeviceItem("QR_Scanner", "Keyence SR-1000", "SCAN_REQ, 1, MODE=AUTO, END"));
                SelectedDevice = Devices[0];
                IsConnected = true;
                Console.Add(new CameraConsoleLine("SCANNER_INIT..."));
                Console.Add(new CameraConsoleLine("ACK READY", isAck: true));
            }
        }

        protected override void OnTrigger()
        {
            Console.Add(new CameraConsoleLine("SCAN_TRIGGER"));
            Console.Add(new CameraConsoleLine("ACK CODE 8901234567890", isAck: true));
        }
    }
}
