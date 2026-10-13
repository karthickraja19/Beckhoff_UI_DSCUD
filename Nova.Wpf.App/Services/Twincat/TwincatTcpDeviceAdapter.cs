using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nova.Abstractions.Hardware.Interfaces;
using Teal.Library.TcpIp;
using Nova.Alarm;
using Nova.Wpf.App.Services.Twincat.HmiDisplay;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// Nova <see cref="INetworkProtocol"/> over one Beckhoff library TCP/IP device (scanner,
    /// vision or robot) - what the Connect, Disconnect and Trigger buttons of the TCP/IP page
    /// call. The PLC owns the connection to the device: Connect and Trigger set the PLC block's
    /// hmicmd bits, and the reply is the string the PLC received.
    ///
    /// Unlike the other adapters this does not hold <see cref="TwincatPlcConnection.SyncRoot"/>:
    /// the library waits inside Connect and TriggerAndRead for up to its timeout, which would
    /// stall every other device. Each library call is a single ADS read or write, which the
    /// ADS client serialises itself, so the calls run on a worker thread instead.
    ///
    /// Whether the device is connected is status: the device's status struct, subscribed
    /// through <see cref="PlcSubscriptions"/> for the life of the HMI, because the Home page's
    /// connection list shows every device's, not only the one a tab shows.
    /// </summary>
    public sealed class TwincatTcpDeviceAdapter : INetworkProtocol, ITwincatStatusSource
    {
        private readonly TcpDevice _device;
        private readonly TwincatPlcConnection _plc;
        private readonly HmiDisplayStatus _display;

        /// <param name="displayType">
        /// The struct App.config names for this kind of device (VisionHmiDisplayType or
        /// ScannerHmiDisplayType); null when it names none that fits.
        /// </param>
        public TwincatTcpDeviceAdapter(TcpDevice device, TwincatPlcConnection plc, PlcSubscriptions subscriptions, Type displayType)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));

            // The library has no Update...Properties for a TCP device, so there is no read
            // through it first; the subscription is the only source.
            _display = new HmiDisplayStatus(subscriptions, plc, device, displayType, null);
            _display.Watch();
        }

        public string Name => _device.EnumName;

        public string ComponentId => _device.EnumName;

        public IReadOnlyList<string> WatchedSymbols => _display.WatchedSymbols;

        /// <summary>Connected, as the PLC last pushed it. False while the PLC is not there.</summary>
        public bool IsConnected
        {
            get
            {
                INetworkHmiDisplay display;
                string reason;
                return _display.TryRead(out display, out reason) && display.Connected;
            }
        }

        public bool Connect() => _plc.IsConnected && _device.Connect();

        public Task<bool> ConnectAsync() => Task.Run(() => Connect());

        /// <summary>The library waits for the PLC to report the link closed, and the tabs call this on the UI thread, so it runs on a worker thread.</summary>
        public void Disconnect()
        {
            if (_plc.IsConnected)
                Task.Run(() => _device.Disconnect());
        }

        /// <summary>
        /// Sends the device's trigger. A vision takes the message as its command string; a
        /// scanner's trigger is fixed in the PLC and a robot's is Start, so they ignore it.
        /// </summary>
        public bool Send(string message) => _plc.IsConnected && _device.Trigger(message);

        public Task<bool> SendAsync(string message) => Task.Run(() => Send(message));

        /// <summary>The string the PLC last received from the device.</summary>
        public string Receive(int bufferSize = 1024) => _plc.IsConnected ? _device.ReceivedData : string.Empty;

        public Task<string> ReceiveAsync(int bufferSize = 1024) => Task.Run(() => Receive(bufferSize));

        /// <summary>
        /// Triggers and waits for the reply. Empty when nothing came back in time. How long it
        /// waits is the device's own ResponseTimeout from its file, just over the PLC's trigger
        /// timeout, so the caller's timeout and poll interval are not used.
        /// </summary>
        public Task<string> SendAndReceiveAsync(string message, int timeoutMs = 4000, int pollIntervalMs = 100)
        {
            if (!_plc.IsConnected)
                return Task.FromResult(string.Empty);

            return Task.Run(() => _device.TriggerAndRead(message));
        }

        public void ClearBufferData()
        {
            if (_plc.IsConnected)
                _device.ClearBuffer();
        }
    }
}
