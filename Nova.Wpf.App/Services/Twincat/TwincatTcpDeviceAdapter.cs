using System;
using System.Threading.Tasks;
using Nova.Abstractions.Hardware.Interfaces;
using Teal.Library.TcpIp;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// Nova <see cref="INetworkProtocol"/> over one Beckhoff library TCP/IP device (scanner,
    /// vision or robot). The PLC owns the socket: Connect and Trigger set the function block's hmicmd
    /// bits and the reply is the string the PLC received, so the console tabs talk to the
    /// device through the PLC rather than opening a connection of their own.
    ///
    /// Unlike the other adapters this does not hold <see cref="TwincatPlcConnection.SyncRoot"/>:
    /// the library waits inside Connect and TriggerAndRead for up to its timeout, which would
    /// stall every other device's polling. Each library call is a single ADS read or write,
    /// which the ADS client serialises itself.
    /// </summary>
    public sealed class TwincatTcpDeviceAdapter : INetworkProtocol
    {
        private readonly TcpDevice _device;
        private readonly TwincatPlcConnection _plc;

        public TwincatTcpDeviceAdapter(TcpDevice device, TwincatPlcConnection plc)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
        }

        public string Name => _device.EnumName;

        public bool IsConnected => _plc.IsConnected && _device.IsConnected;

        public bool Connect() => _plc.IsConnected && _device.Connect();

        public Task<bool> ConnectAsync() => Task.Run(() => Connect());

        /// <summary>
        /// The library waits for the PLC to report the socket closed, and the console tabs
        /// call this on the UI thread, so it is not waited on here.
        /// </summary>
        public void Disconnect()
        {
            if (_plc.IsConnected)
                _device.DisconnectAsync();
        }

        /// <summary>
        /// Sends the device's trigger. A vision takes the message as its command string. A
        /// scanner's trigger is fixed in the PLC and a robot's is Start, so for those the
        /// message is ignored.
        /// </summary>
        public bool Send(string message)
        {
            if (!_plc.IsConnected)
                return false;

            var vision = _device as Vision;
            return vision != null && !string.IsNullOrEmpty(message)
                ? vision.Trigger(message)
                : _device.Trigger();
        }

        public Task<bool> SendAsync(string message) => Task.Run(() => Send(message));

        /// <summary>The string the PLC last received from the device.</summary>
        public string Receive(int bufferSize = 1024) => _plc.IsConnected ? _device.ReceivedData : string.Empty;

        public Task<string> ReceiveAsync(int bufferSize = 1024) => Task.Run(() => Receive(bufferSize));

        /// <summary>Triggers and waits for the reply. Empty when nothing came back in time.</summary>
        public Task<string> SendAndReceiveAsync(string message, int timeoutMs = 4000, int pollIntervalMs = 100)
        {
            if (!_plc.IsConnected)
                return Task.FromResult(string.Empty);

            var vision = _device as Vision;
            return vision != null && !string.IsNullOrEmpty(message)
                ? vision.ReadResultAsync(message, timeoutMs, pollIntervalMs)
                : _device.TriggerAndReadAsync(timeoutMs, pollIntervalMs);
        }

        public void ClearBufferData()
        {
            if (_plc.IsConnected)
                _device.ClearBuffer();
        }
    }
}
