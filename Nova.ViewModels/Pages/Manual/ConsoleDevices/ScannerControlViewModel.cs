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

        public ScannerControlViewModel(ICommDeviceService comm, IConnectionStatusService connectionStatus = null)
            : base(connectionStatus)
        {
            LoadDevices(comm?.GetDevices(CommDeviceKind.Scanner));
        }
    }
}
