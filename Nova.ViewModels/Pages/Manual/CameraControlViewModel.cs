using System;
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
    /// <see cref="ICommDeviceService"/>; Connect/Trigger/Disconnect do real TCP.
    ///
    /// The connection status is the CCD's own: read when the tab comes on screen and when
    /// another CCD is selected, and followed through
    /// <see cref="IConnectionStatusService.StatusChanged"/> while the tab is on screen.
    /// </summary>
    public partial class CameraControlViewModel : ViewModelBase, IActivatableTab
    {
        private readonly IConnectionStatusService _connectionStatus;
        private bool _isActive;

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

        public CameraControlViewModel(ICommDeviceService comm, IConnectionStatusService connectionStatus = null)
        {
            _connectionStatus = connectionStatus;
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

            RefreshConnection();
        }

        private INetworkProtocol ActiveProtocol => SelectedDevice?.Protocol;

        partial void OnSelectedDeviceChanged(CcdDeviceViewModel value) => RefreshConnection();

        public void Activate()
        {
            if (_isActive)
                return;

            _isActive = true;
            if (_connectionStatus != null)
                _connectionStatus.StatusChanged += OnConnectionStatusChanged;
            RefreshConnection();
        }

        public void Deactivate()
        {
            if (!_isActive)
                return;

            _isActive = false;
            if (_connectionStatus != null)
                _connectionStatus.StatusChanged -= OnConnectionStatusChanged;
        }

        // Raised on the UI thread when any device's connection changes.
        private void OnConnectionStatusChanged(object sender, EventArgs e) => RefreshConnection();

        /// <summary>Shows the selected CCD's connection as it reports it.</summary>
        private void RefreshConnection()
        {
            var p = ActiveProtocol;
            IsConnected = p != null && p.IsConnected;
        }

        [RelayCommand]
        private async Task ConnectDevice()
        {
            if (IsConnected) return;
            Console.Add(new CameraConsoleLine("CONNECT " + (SelectedDevice?.Name ?? "CCD") + "..."));
            var p = ActiveProtocol;
            bool ok = p != null && await p.ConnectAsync();
            // The CCD's own status can arrive a moment after the connect returns; the
            // status change then confirms it.
            IsConnected = ok || (p != null && p.IsConnected);
            Console.Add(new CameraConsoleLine(ok ? "ACK LINK_ESTABLISHED" : "CONNECT_FAILED", isAck: ok));
        }

        [RelayCommand]
        private async Task TriggerCapture()
        {
            if (!IsConnected) return;
            var p = ActiveProtocol;
            if (p == null)
                return;
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
            // The PLC closes the link in the background, so the CCD can still report
            // connected here; the status change shows it closed.
            RefreshConnection();
            Console.Add(new CameraConsoleLine("DISCONNECT"));
            await Task.CompletedTask;
        }

        [RelayCommand]
        private void RefreshResponse()
        {
            Console.Clear();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !IsDisposed)
                Deactivate();
            base.Dispose(disposing);
        }

    }
}
