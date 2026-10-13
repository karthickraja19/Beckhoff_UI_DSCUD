using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using Nova.Abstractions.Services;

namespace Nova.Core.Services
{
    /// <summary>
    /// Home screen Connection Status: the configured comm devices with their link state,
    /// plus the PLC. The PLC entry comes from the host - the ADS connection.
    /// </summary>
    public sealed class ConnectionStatusService : IConnectionStatusService, IDisposable
    {
        private const int PollMs = 1000;

        private static readonly CommDeviceKind[] Kinds =
        {
            CommDeviceKind.Ccd, CommDeviceKind.Scanner, CommDeviceKind.Sfc
        };

        private readonly ICommDeviceService _commDevices;
        private readonly string _plcName;
        private readonly Func<bool> _plcConnected;
        private readonly object _sync = new object();
        private Timer _timer;
        private string _lastSignature;

        public event EventHandler StatusChanged;

        public ConnectionStatusService(ICommDeviceService commDevices, string plcName, Func<bool> plcConnected)
        {
            _commDevices = commDevices ?? throw new ArgumentNullException(nameof(commDevices));
            _plcName = plcName;
            _plcConnected = plcConnected ?? (() => false);
        }

        public IReadOnlyList<ConnectionStatusEntry> Read()
        {
            var entries = new List<ConnectionStatusEntry>();

            foreach (var kind in Kinds)
            {
                foreach (var device in _commDevices.GetDevices(kind) ?? new CommDevice[0])
                {
                    entries.Add(new ConnectionStatusEntry
                    {
                        DeviceType = DeviceTypeName(kind),
                        Name = device.Name,
                        IsConnected = device.Protocol != null && device.Protocol.IsConnected,
                    });
                }
            }

            entries.Add(new ConnectionStatusEntry
            {
                DeviceType = "PLC",
                Name = _plcName,
                IsConnected = _plcConnected(),
            });

            return entries;
        }

        public void Start()
        {
            lock (_sync)
            {
                if (_timer == null)
                    _timer = new Timer(OnTick, null, 0, PollMs);
            }
        }

        public void Stop()
        {
            lock (_sync)
            {
                _timer?.Dispose();
                _timer = null;
            }
        }

        public void Dispose() => Stop();

        private void OnTick(object state)
        {
            try
            {
                string signature = string.Join(";", Read().Select(e => e.DeviceType + "|" + e.Name + "|" + e.IsConnected));
                lock (_sync)
                {
                    if (signature == _lastSignature)
                        return;
                    _lastSignature = signature;
                }

                var app = Application.Current;
                if (app == null)
                    return; // shutting down

                app.Dispatcher.BeginInvoke(new Action(() => StatusChanged?.Invoke(this, EventArgs.Empty)));
            }
            catch
            {
                // A status read must never kill the poll.
            }
        }

        private static string DeviceTypeName(CommDeviceKind kind)
        {
            switch (kind)
            {
                case CommDeviceKind.Ccd: return "CCD";
                case CommDeviceKind.Scanner: return "Scanner";
                case CommDeviceKind.Sfc: return "SFC";
                default: return kind.ToString();
            }
        }
    }
}
