using System;
using System.Collections.Generic;
using Nova.Abstractions.Hardware;
using Nova.Abstractions.Services;
using Teal.Library.Log;
using Nova.Alarm;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// Live axis and stepper status. Nothing is polled: each
    /// adapter's status symbols are subscribed through <see cref="PlcSubscriptions"/>,
    /// and when the PLC pushes a value this publishes a fresh snapshot of every device that
    /// value belongs to. A device is also published when the library has read its current
    /// status on a screen opening it (<see cref="ILibraryStatusSource"/>). When the
    /// subscription comes or goes (PLC back in Run, a new PLC program, the connection lost)
    /// every device is published, so the screens say why there is no live status instead of
    /// keeping the last values.
    /// </summary>
    public sealed class TwincatAxisMonitorService : IAxisMonitorService, IDisposable
    {
        private readonly PlcSubscriptions _subscriptions;
        private readonly List<Device> _devices = new List<Device>();
        private readonly Dictionary<string, List<Device>> _bySymbol = new Dictionary<string, List<Device>>(StringComparer.OrdinalIgnoreCase);
        private readonly object _sync = new object();
        private bool _started;

        /// <summary>
        /// Raised on the ADS thread (a worker thread when the subscription comes or goes),
        /// so handlers must be quick and must not touch the PLC.
        /// </summary>
        public event EventHandler<AxisStatusSnapshot> AxisStatusUpdated;

        public TwincatAxisMonitorService(
            PlcSubscriptions subscriptions, IEnumerable<IAxis> axes, IEnumerable<IStepper> steppers)
        {
            _subscriptions = subscriptions ?? throw new ArgumentNullException(nameof(subscriptions));

            foreach (var axis in axes ?? new IAxis[0])
                Add(axis as ITwincatStatusSource, axis.ReadStatus);

            foreach (var stepper in steppers ?? new IStepper[0])
                Add(stepper as ITwincatStatusSource, stepper.ReadStatus);
        }

        private void Add(ITwincatStatusSource source, Func<AxisStatusSnapshot> read)
        {
            var device = new Device(read, source as ILibraryStatusSource);
            device.OnLibraryStatusRead = (sender, e) => Publish(device);
            _devices.Add(device);

            if (source == null)
                return;

            foreach (string symbol in source.WatchedSymbols)
            {
                List<Device> list;
                if (!_bySymbol.TryGetValue(symbol, out list))
                    _bySymbol[symbol] = list = new List<Device>();

                list.Add(device);
            }
        }

        public void Start()
        {
            lock (_sync)
            {
                if (_started)
                    return;

                _started = true;
                _subscriptions.ValueChanged += OnValueChanged;
                _subscriptions.LiveChanged += OnLiveChanged;
                foreach (var device in _devices)
                {
                    if (device.Library != null)
                        device.Library.LibraryStatusRead += device.OnLibraryStatusRead;
                }
            }

            // What each device shows until the PLC pushes something.
            PublishAll();
        }

        public void Stop()
        {
            lock (_sync)
            {
                if (!_started)
                    return;

                _started = false;
                _subscriptions.ValueChanged -= OnValueChanged;
                _subscriptions.LiveChanged -= OnLiveChanged;
                foreach (var device in _devices)
                {
                    if (device.Library != null)
                        device.Library.LibraryStatusRead -= device.OnLibraryStatusRead;
                }
            }
        }

        /// <summary>The subscriptions belong to the application, which stops them.</summary>
        public void Dispose()
        {
            Stop();
        }

        private void OnValueChanged(object sender, string symbol)
        {
            List<Device> devices;
            if (!_bySymbol.TryGetValue(symbol, out devices))
                return;   // a cylinder's, a vacuum's or a TCP device's

            foreach (var device in devices)
                Publish(device);
        }

        private void OnLiveChanged(object sender, EventArgs e)
        {
            PublishAll();
        }

        private void PublishAll()
        {
            foreach (var device in _devices)
                Publish(device);
        }

        private void Publish(Device device)
        {
            try
            {
                AxisStatusSnapshot snapshot = device.Read();
                if (snapshot != null)
                    AxisStatusUpdated?.Invoke(this, snapshot);
            }
            catch (Exception ex)
            {
                // A faulty screen must not stop the other devices being reported.
                Logger.WriteLog(LogType.Error, "Axis", "PLC", "An axis status handler failed: " + ex.Message, ex.ToString());
            }
        }

        private sealed class Device
        {
            public Device(Func<AxisStatusSnapshot> read, ILibraryStatusSource library)
            {
                Read = read;
                Library = library;
            }

            public Func<AxisStatusSnapshot> Read { get; }

            /// <summary>The adapter, when the library reads its status on a screen opening it; otherwise null.</summary>
            public ILibraryStatusSource Library { get; }

            /// <summary>Set by the monitor: publishes this device.</summary>
            public EventHandler OnLibraryStatusRead;
        }
    }
}
