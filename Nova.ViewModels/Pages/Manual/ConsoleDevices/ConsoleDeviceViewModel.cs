using System;
using System.Collections.Generic;
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
    /// Shared shell for the console-style Manual device tabs (Scanner, CCD,
    /// SFC &amp; PDCA): a device selector + live connection status, a "Send
    /// Command" editor, a "Received Response" console and Connect / Trigger /
    /// Disconnect actions.
    ///
    /// Connect/Trigger/Disconnect route through <see cref="DoConnectAsync"/> /
    /// <see cref="DoTriggerAsync"/> / <see cref="DoDisconnectAsync"/>. The default
    /// implementations resolve the selected device's <see cref="INetworkProtocol"/>
    /// (loaded via <see cref="LoadDevices"/>) and send/receive through it. A device with
    /// no transport cannot be connected.
    ///
    /// The connection status is the device's own: it is read when the tab comes on screen
    /// and when another device is selected, and followed through
    /// <see cref="IConnectionStatusService.StatusChanged"/> while the tab is on screen. So a
    /// device that is already connected shows CONNECTED, with Connect disabled and the
    /// other buttons enabled, without pressing Connect first.
    /// </summary>
    public abstract partial class ConsoleDeviceViewModel : ViewModelBase, IActivatableTab
    {
        private readonly Dictionary<string, INetworkProtocol> _protocols =
            new Dictionary<string, INetworkProtocol>();

        private readonly IConnectionStatusService _connectionStatus;
        private bool _isActive;

        protected ConsoleDeviceViewModel(IConnectionStatusService connectionStatus = null)
        {
            _connectionStatus = connectionStatus;
        }

        public ObservableCollection<ConsoleDeviceItem> Devices { get; } = new ObservableCollection<ConsoleDeviceItem>();

        [ObservableProperty]
        private ConsoleDeviceItem _selectedDevice;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanConnect))]
        [NotifyPropertyChangedFor(nameof(StatusText))]
        private bool _isConnected;

        [ObservableProperty]
        private string _sendCommand;

        /// <summary>Shown while no device is selected (and for a tab with no devices at all).</summary>
        private readonly ObservableCollection<CameraConsoleLine> _noDevice = new ObservableCollection<CameraConsoleLine>();

        /// <summary>
        /// The selected device's own Received Response history. FAM 2 Bug_Id_038 / MS_11 /
        /// MS_16: one console used to hold every device's answers and kept them across a change.
        /// </summary>
        public ObservableCollection<CameraConsoleLine> Console => SelectedDevice != null ? SelectedDevice.Console : _noDevice;

        /// <summary>
        /// What the console shows when a command got no answer. FAM 2 Bug_Id_039: the device
        /// sends nothing back for a command it does not know, so the line says so.
        /// </summary>
        public const string NoResponseText = "No response: invalid command, or the device did not answer.";

        public bool CanConnect => !IsConnected;
        public string StatusText => IsConnected ? "CONNECTED" : "DISCONNECTED";

        // ---- Per-tab captions / labels (overridden by subclasses) ---------
        public virtual string SelectorCaption => "Select Device";
        public virtual string SendCaption => "SEND COMMAND";
        public virtual string DeviceCaption => "SELECTED DEVICE";

        public virtual string TriggerLabel => "TRIGGER";
        public virtual string TriggerIcon => "Play";

        /// <summary>The transport for the current selection, or null when it has none.</summary>
        protected INetworkProtocol ActiveProtocol
            => SelectedDevice != null && _protocols.TryGetValue(SelectedDevice.Name, out var p) ? p : null;

        /// <summary>True when at least one transport is wired.</summary>
        protected bool HasTransports => _protocols.Count > 0;

        /// <summary>Populate Devices + transports from the config-driven registry.</summary>
        protected void LoadDevices(IReadOnlyList<CommDevice> devices)
        {
            if (devices == null) return;
            foreach (var d in devices)
            {
                Devices.Add(new ConsoleDeviceItem(d.Name, d.Detail, d.DefaultCommand));
                if (d.Protocol != null)
                    _protocols[d.Name] = d.Protocol;
            }
            if (Devices.Count > 0)
                SelectedDevice = Devices[0];
        }

        partial void OnSelectedDeviceChanged(ConsoleDeviceItem value) => OnDeviceSelected(value);

        /// <summary>
        /// Hook for subclasses to react to a selection. Clears the command editor rather
        /// than pre-loading anything into it.
        ///
        /// Selecting a device used to drop that device's default command straight into
        /// the Send Command box, across Scanner, CCD, Robot and SFC &amp; PDCA
        /// tabs. Removed by request: a command sitting in the box ready to send is one
        /// keypress from being sent to real hardware, and after switching devices it was
        /// the *previous* device's command left in view. Clearing on every change means
        /// what is in the box is always something the operator typed for the device
        /// currently selected.
        /// </summary>
        protected virtual void OnDeviceSelected(ConsoleDeviceItem item)
        {
            SendCommand = string.Empty;
            OnPropertyChanged(nameof(Console));
            RefreshConnection();
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

        /// <summary>Shows the selected device's connection as the device reports it.</summary>
        protected void RefreshConnection()
        {
            IsConnected = ReadDeviceConnected();
        }

        /// <summary>
        /// Whether the device the buttons act on is connected, as it reports it. False when
        /// it has no transport.
        /// </summary>
        protected virtual bool ReadDeviceConnected()
        {
            var p = ActiveProtocol;
            return p != null && p.IsConnected;
        }

        [RelayCommand]
        private async Task ConnectDevice()
        {
            if (IsConnected) return;
            // The device's own history, captured now: the selection may change during the await.
            var console = Console;
            console.Add(new CameraConsoleLine("CONNECT " + (SelectedDevice?.Name ?? "DEVICE") + "..."));
            bool ok = await DoConnectAsync();
            // The device's own status can arrive a moment after the connect returns; the
            // status change then confirms it.
            IsConnected = ok || ReadDeviceConnected();
            console.Add(new CameraConsoleLine(ok ? "ACK LINK_ESTABLISHED" : "CONNECT_FAILED", isAck: ok));
            if (ok) OnConnected();
        }

        [RelayCommand]
        private async Task Trigger()
        {
            if (!IsConnected) return;
            var console = Console;
            string command = SendCommand;
            string response = await DoTriggerAsync() ?? string.Empty;
            if (!string.IsNullOrEmpty(command))
                console.Add(new CameraConsoleLine(command));
            console.Add(new CameraConsoleLine(
                response.Length == 0 ? NoResponseText : response.Trim(),
                isAck: response.Length > 0));
        }

        [RelayCommand]
        private async Task Disconnect()
        {
            if (!IsConnected) return;
            var console = Console;
            await DoDisconnectAsync();
            // A TCP device closes its link in the background, so it can still report
            // connected here; the status change shows it closed.
            RefreshConnection();
            console.Add(new CameraConsoleLine("DISCONNECT"));
        }

       
[RelayCommand]
private void RefreshResponse()
        {
            Console.Clear();
        }



        // ---- Transport hooks (socket subclasses use ActiveProtocol) ----

        protected virtual async Task<bool> DoConnectAsync()
        {
            var p = ActiveProtocol;
            return p != null && await p.ConnectAsync();
        }

        /// <summary>Returns the device response; empty when there was none.</summary>
        protected virtual async Task<string> DoTriggerAsync()
        {
            var p = ActiveProtocol;
            return p == null ? string.Empty : await p.SendAndReceiveAsync(SendCommand);
        }

        protected virtual Task DoDisconnectAsync()
        {
            ActiveProtocol?.Disconnect();
            return Task.CompletedTask;
        }

        /// <summary>Subclass hook after a successful connect.</summary>
        protected virtual void OnConnected() { }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !IsDisposed)
                Deactivate();
            base.Dispose(disposing);
        }
    }
}
