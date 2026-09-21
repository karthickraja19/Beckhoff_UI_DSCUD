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
    /// Shared shell for the console-style Manual device tabs (Scanner, Light Control,
    /// SFC &amp; PDCA, Robot): a device selector + live connection status, a "Send
    /// Command" editor, a "Received Response" console and Connect / Trigger /
    /// Disconnect actions.
    ///
    /// Connect/Trigger/Disconnect route through <see cref="DoConnectAsync"/> /
    /// <see cref="DoTriggerAsync"/> / <see cref="DoDisconnectAsync"/>. The default
    /// implementations resolve the selected device's <see cref="INetworkProtocol"/>
    /// (loaded via <see cref="LoadDevices"/>) and do real TCP send/receive; when no
    /// transport is configured they fall back to the legacy seeded-console behaviour,
    /// so a tab still demos on a dev PC with no config.
    /// </summary>
    public abstract partial class ConsoleDeviceViewModel : ViewModelBase
    {
        private readonly Dictionary<string, INetworkProtocol> _protocols =
            new Dictionary<string, INetworkProtocol>();

        public ObservableCollection<ConsoleDeviceItem> Devices { get; } = new ObservableCollection<ConsoleDeviceItem>();

        [ObservableProperty]
        private ConsoleDeviceItem _selectedDevice;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanConnect))]
        [NotifyPropertyChangedFor(nameof(StatusText))]
        private bool _isConnected;

        [ObservableProperty]
        private string _sendCommand;

        public ObservableCollection<CameraConsoleLine> Console { get; } = new ObservableCollection<CameraConsoleLine>();

        public bool CanConnect => !IsConnected;
        public string StatusText => IsConnected ? "CONNECTED" : "DISCONNECTED";

        // ---- Per-tab captions / labels (overridden by subclasses) ---------
        public virtual string SelectorCaption => "Select Device";
        public virtual string SendCaption => "SEND COMMAND";
        public virtual string DeviceCaption => "SELECTED DEVICE";

        /// <summary>
        /// Set by tabs that need a second dropdown next to the device selector — the
        /// Light Control tab picks a light object and a colour recipe, mirroring V3's
        /// cmbCOMPort + cmbOPTSend pair. Hidden for every other console tab.
        /// </summary>
        public virtual bool HasSecondarySelector => false;

        public virtual string SecondarySelectorCaption => "";
        public virtual string TriggerLabel => "TRIGGER";
        public virtual string TriggerIcon => "Play";

        /// <summary>The transport for the current selection, or null when unconfigured (fake mode).</summary>
        protected INetworkProtocol ActiveProtocol
            => SelectedDevice != null && _protocols.TryGetValue(SelectedDevice.Name, out var p) ? p : null;

        /// <summary>True when at least one real transport is wired (skips legacy fake seeding).</summary>
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
        /// the Send Command box, across Scanner, CCD, Robot, SFC &amp; PDCA and Light
        /// Control. Removed by request: a command sitting in the box ready to send is one
        /// keypress from being sent to real hardware, and after switching devices it was
        /// the *previous* device's command left in view. Clearing on every change means
        /// what is in the box is always something the operator typed for the device
        /// currently selected.
        /// </summary>
        protected virtual void OnDeviceSelected(ConsoleDeviceItem item)
        {
            SendCommand = string.Empty;
        }

        [RelayCommand]
        private async Task ConnectDevice()
        {
            if (IsConnected) return;
            Console.Add(new CameraConsoleLine("CONNECT " + (SelectedDevice?.Name ?? "DEVICE") + "..."));
            bool ok = await DoConnectAsync();
            IsConnected = ok;
            Console.Add(new CameraConsoleLine(ok ? "ACK LINK_ESTABLISHED" : "CONNECT_FAILED", isAck: ok));
            if (ok) OnConnected();
        }

        [RelayCommand]
        private async Task Trigger()
        {
            if (!IsConnected) return;
            string response = await DoTriggerAsync();
            if (response != null)
            {
                if (!string.IsNullOrEmpty(SendCommand))
                    Console.Add(new CameraConsoleLine(SendCommand));
                Console.Add(new CameraConsoleLine(
                    response.Length == 0 ? "(no response)" : response.Trim(),
                    isAck: response.Length > 0));
            }
            else
            {
                OnTrigger(); // legacy fake fallback
            }
        }

        [RelayCommand]
        private async Task Disconnect()
        {
            if (!IsConnected) return;
            await DoDisconnectAsync();
            IsConnected = false;
            Console.Add(new CameraConsoleLine("DISCONNECT"));
            Console.Add(new CameraConsoleLine("LINK_CLOSED"));
        }

       
[RelayCommand]
private void RefreshResponse()
        {
            Console.Clear();
        }



        // ---- Transport hooks (socket subclasses use ActiveProtocol; light overrides) ----

        protected virtual async Task<bool> DoConnectAsync()
        {
            var p = ActiveProtocol;
            return p == null ? true : await p.ConnectAsync();
        }

        /// <summary>Returns the device response, or null to fall back to the legacy fake OnTrigger().</summary>
        protected virtual async Task<string> DoTriggerAsync()
        {
            var p = ActiveProtocol;
            return p == null ? null : await p.SendAndReceiveAsync(SendCommand);
        }

        protected virtual Task DoDisconnectAsync()
        {
            ActiveProtocol?.Disconnect();
            return Task.CompletedTask;
        }

        /// <summary>Subclass hook after a successful connect.</summary>
        protected virtual void OnConnected() { }

        /// <summary>Legacy fake Trigger action, used only when no real transport is configured.</summary>
        protected virtual void OnTrigger() { }
    }
}
