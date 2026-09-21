using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Hardware.Interfaces;
using Nova.Abstractions.Services;
using Nova.ViewModels.Common;

namespace Nova.ViewModels.Pages.Manual
{
    /// <summary>
    /// ViewModel backing the "Camera" tab of the Manual screen — a TCP camera/CCD
    /// console mirroring the V3 flow (select CCD, connect, edit the outbound command,
    /// trigger a capture, watch the response). Devices come from CCDs.json via
    /// <see cref="ICommDeviceService"/>; Connect/Trigger/Disconnect do real TCP, with a
    /// seeded dev fallback when no config is present.
    /// </summary>
    public partial class CameraControlViewModel : ViewModelBase
    {
        public ObservableCollection<CcdDeviceViewModel> Devices { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanConnect))]
        [NotifyPropertyChangedFor(nameof(StatusText))]
        private bool _isConnected;

        [ObservableProperty]
        private CcdDeviceViewModel _selectedDevice;

        [ObservableProperty]
        private string _sendCommand;

        public ObservableCollection<CameraConsoleLine> Console { get; }

        public bool CanConnect => !IsConnected;
        public string StatusText => IsConnected ? "CONNECTED" : "DISCONNECTED";

        public CameraControlViewModel(ICommDeviceService comm)
        {
            Devices = new ObservableCollection<CcdDeviceViewModel>();
            Console = new ObservableCollection<CameraConsoleLine>();

            var ccds = comm?.GetDevices(CommDeviceKind.Ccd);
            _sendCommand = "AUTO_MODE, 1.0, Auto_mode_Selected, 999.999, 1, IN, END";

            if (ccds != null && ccds.Count > 0)
            {
                foreach (var d in ccds)
                    Devices.Add(new CcdDeviceViewModel(d.Name, d.Detail, d.Protocol));
                _selectedDevice = Devices[0];
                _sendCommand = ccds[0].DefaultCommand ?? _sendCommand;
            }
            else
            {
                // Dev fallback (no config): seeded handshake so the console reads live.
                Devices.Add(new CcdDeviceViewModel("CCD1", "Sony IMX250"));
                Devices.Add(new CcdDeviceViewModel("CCD3", "Sony IMX264"));
                _selectedDevice = Devices[0];
                _isConnected = true;
                Console.Add(new CameraConsoleLine("CAM_INIT_REQ..."));
                Console.Add(new CameraConsoleLine("ACK INIT_SUCCESS", isAck: true));
                Console.Add(new CameraConsoleLine("GET_STATUS"));
                Console.Add(new CameraConsoleLine("TEMP: 42°C | BUFFER: 2%"));
            }
        }

        private INetworkProtocol ActiveProtocol => SelectedDevice?.Protocol;

        [RelayCommand]
        private async Task ConnectDevice()
        {
            if (IsConnected) return;
            Console.Add(new CameraConsoleLine("CONNECT " + (SelectedDevice?.Name ?? "CCD") + "..."));
            var p = ActiveProtocol;
            bool ok = p == null ? true : await p.ConnectAsync();
            IsConnected = ok;
            Console.Add(new CameraConsoleLine(ok ? "ACK LINK_ESTABLISHED" : "CONNECT_FAILED", isAck: ok));
        }

        [RelayCommand]
        private async Task TriggerCapture()
        {
            if (!IsConnected) return;
            var p = ActiveProtocol;
            if (p == null)
            {
                Console.Add(new CameraConsoleLine("TRIGGER_CAPTURE"));
                Console.Add(new CameraConsoleLine("ACK CAPTURE_DONE", isAck: true));
                return;
            }
            Console.Add(new CameraConsoleLine(SendCommand));
            string response = await p.SendAndReceiveAsync(SendCommand);
            Console.Add(new CameraConsoleLine(
                string.IsNullOrEmpty(response) ? "(no response)" : response.Trim(),
                isAck: !string.IsNullOrEmpty(response)));
        }

        [RelayCommand]
        private async Task Disconnect()
        {
            if (!IsConnected) return;
            ActiveProtocol?.Disconnect();
            IsConnected = false;
            Console.Add(new CameraConsoleLine("DISCONNECT"));
            Console.Add(new CameraConsoleLine("LINK_CLOSED"));
            await Task.CompletedTask;
        }

        [RelayCommand]
        private void RefreshResponse()
        {
            Console.Clear();
        }

    }
}
