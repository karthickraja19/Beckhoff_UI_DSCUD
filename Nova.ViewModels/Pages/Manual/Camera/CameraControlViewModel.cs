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

        /// <summary>Shown while no CCD is selected.</summary>
        private readonly ObservableCollection<CameraConsoleLine> _noDevice = new ObservableCollection<CameraConsoleLine>();

        /// <summary>The selected CCD's own Received Response history (FAM 2 Bug_Id_038 / MS_16).</summary>
        public ObservableCollection<CameraConsoleLine> Console => SelectedDevice != null ? SelectedDevice.Console : _noDevice;

        public bool CanConnect => !IsConnected;
        public string StatusText => IsConnected ? "CONNECTED" : "DISCONNECTED";

        private const string FallbackCommand = "AUTO_MODE, 1.0, Auto_mode_Selected, 999.999, 1, IN, END";

        public CameraControlViewModel(ICommDeviceService comm, IConnectionStatusService connectionStatus = null)
        {
            _connectionStatus = connectionStatus;
            Devices = new ObservableCollection<CcdDeviceViewModel>();

            var ccds = comm?.GetDevices(CommDeviceKind.Ccd);
            _sendCommand = FallbackCommand;

            if (ccds != null && ccds.Count > 0)
            {
                foreach (var d in ccds)
                    Devices.Add(new CcdDeviceViewModel(d.Name, d.Detail, d.Protocol, d.DefaultCommand ?? FallbackCommand));
                _selectedDevice = Devices[0];
                _sendCommand = Devices[0].SendCommand;
            }

            RefreshConnection();
        }

        private INetworkProtocol ActiveProtocol => SelectedDevice?.Protocol;

        /// <summary>The CCD being left keeps what was typed for it; the new one shows its own command and history.</summary>
        partial void OnSelectedDeviceChanged(CcdDeviceViewModel oldValue, CcdDeviceViewModel newValue)
        {
            if (oldValue != null)
                oldValue.SendCommand = SendCommand;

            SendCommand = newValue != null ? newValue.SendCommand : string.Empty;
            OnPropertyChanged(nameof(Console));
            RefreshConnection();
        }

        partial void OnSendCommandChanged(string value)
        {
            if (SelectedDevice != null)
                SelectedDevice.SendCommand = value;
        }

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
            // The CCD's own history, captured now: the selection may change during the await.
            var console = Console;
            var p = ActiveProtocol;
            console.Add(new CameraConsoleLine("CONNECT " + (SelectedDevice?.Name ?? "CCD") + "..."));
            bool ok = p != null && await p.ConnectAsync();
            // The CCD's own status can arrive a moment after the connect returns; the
            // status change then confirms it.
            RefreshConnection();
            if (ok && ReferenceEquals(p, ActiveProtocol))
                IsConnected = true;
            console.Add(new CameraConsoleLine(ok ? "ACK LINK_ESTABLISHED" : "CONNECT_FAILED", isAck: ok));
        }

        [RelayCommand]
        private async Task TriggerCapture()
        {
            if (!IsConnected) return;
            var p = ActiveProtocol;
            if (p == null)
                return;

            var console = Console;
            string command = SendCommand;
            if (string.IsNullOrWhiteSpace(command))
            {
                console.Add(new CameraConsoleLine("Enter a command to send."));
                return;
            }

            console.Add(new CameraConsoleLine(command));
            string response = await p.SendAndReceiveAsync(command);
            console.Add(new CameraConsoleLine(
                string.IsNullOrEmpty(response) ? ConsoleDeviceViewModel.NoResponseText : response.Trim(),
                isAck: !string.IsNullOrEmpty(response)));
        }

        [RelayCommand]
        private async Task Disconnect()
        {
            if (!IsConnected) return;
            var console = Console;
            ActiveProtocol?.Disconnect();
            // The PLC closes the link in the background, so the CCD can still report
            // connected here; the status change shows it closed.
            RefreshConnection();
            console.Add(new CameraConsoleLine("DISCONNECT"));
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
