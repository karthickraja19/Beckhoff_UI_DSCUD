using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Nova.Abstractions.Hardware;
using Nova.Abstractions.Services;
using Nova.ViewModels.Common;

namespace Nova.ViewModels.Pages.Calibration
{
    /// <summary>One device of a calibration's device tab and whether it is connected.</summary>
    public sealed class CalibrationDeviceRow : ObservableObject
    {
        private bool _isConnected;

        public CalibrationDeviceRow(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public bool IsConnected
        {
            get { return _isConnected; }
            set { SetProperty(ref _isConnected, value); }
        }
    }

    /// <summary>
    /// The Camera and Robot tabs of the Config page's Calibration section (V3's
    /// CalibrationCameraView / CalibrationRobotView): the devices a calibration needs,
    /// CCD cameras or robots, and whether each is connected right now.
    /// </summary>
    /// <remarks>
    /// V3's Start / Stop and Connect / Trigger buttons only changed text on screen - no
    /// camera or robot was driven - so they are not carried over: nothing here pretends a
    /// calibration ran. The vision and robot calibrations themselves are run by the PLC
    /// from the other tabs. The state is the Home screen's Connection Status
    /// (<see cref="IConnectionStatusService"/>), the same source, read when it changes.
    /// </remarks>
    public sealed class CalibrationDeviceViewModel : ObservableObject, IActivatableTab, IDisposable
    {
        private readonly string _deviceType;
        private readonly IConnectionStatusService _status;
        private readonly IRobotIOService _robots;      // robots: the PLC's EtherCAT link, read only while followed
        private IDisposable _following;
        private readonly SynchronizationContext _ui = SynchronizationContext.Current;

        public CalibrationDeviceViewModel(string title, string icon, string deviceType, string hint, IConnectionStatusService status, IRobotIOService robots = null)
        {
            _robots = robots;
            Title = title;
            Icon = icon;
            Hint = hint;
            _deviceType = deviceType;
            _status = status;

            if (_robots != null)
                _robots.IOChanged += OnStatusChanged;
            else if (_status != null)
                _status.StatusChanged += OnStatusChanged;

            Refresh();
        }

        public string Title { get; }

        public string Icon { get; }

        /// <summary>What the tab is for, under its title.</summary>
        public string Hint { get; }

        public ObservableCollection<CalibrationDeviceRow> Devices { get; } = new ObservableCollection<CalibrationDeviceRow>();

        public bool HasDevices => Devices.Count > 0;

        public string SummaryText
        {
            get
            {
                if (_status == null && _robots == null)
                    return "The connection status is not available.";
                if (Devices.Count == 0)
                    return "No " + Title.ToLowerInvariant() + " device is configured on this machine.";

                int online = Devices.Count(d => d.IsConnected);
                return online + " of " + Devices.Count + " connected";
            }
        }

        /// <summary>Raised on a background thread: marshalled, because the list is bound.</summary>
        private void OnStatusChanged(object sender, EventArgs e)
        {
            if (_ui == null)
                Refresh();
            else
                _ui.Post(_ => Refresh(), null);
        }

        public void Refresh()
        {
            var entries = _robots != null
                ? _robots.Robots.Select(name =>
                {
                    RobotIOSnapshot snapshot = _robots.Read(name);
                    return new ConnectionStatusEntry { DeviceType = _deviceType, Name = name, IsConnected = snapshot != null && snapshot.IsConnected };
                }).ToList()
                : _status != null
                ? (_status.Read() ?? new ConnectionStatusEntry[0])
                    .Where(x => string.Equals(x.DeviceType, _deviceType, StringComparison.OrdinalIgnoreCase)).ToList()
                : new System.Collections.Generic.List<ConnectionStatusEntry>();

            bool sameList = entries.Count == Devices.Count
                && entries.Select((x, i) => string.Equals(x.Name, Devices[i].Name, StringComparison.Ordinal)).All(same => same);
            if (!sameList)
            {
                Devices.Clear();
                foreach (ConnectionStatusEntry entry in entries)
                    Devices.Add(new CalibrationDeviceRow(entry.Name));
            }

            for (int i = 0; i < entries.Count; i++)
                Devices[i].IsConnected = entries[i].IsConnected;

            OnPropertyChanged(nameof(HasDevices));
            OnPropertyChanged(nameof(SummaryText));
        }

        /// <summary>A robot's link is read from the PLC only while a screen shows it.</summary>
        public void Activate()
        {
            if (_robots is ILiveStatus live && _following == null)
                _following = live.FollowStatus();

            Refresh();
        }

        public void Deactivate()
        {
            IDisposable following = _following;
            _following = null;
            if (following != null)
                following.Dispose();
        }

        public void Dispose()
        {
            Deactivate();
            if (_robots != null)
                _robots.IOChanged -= OnStatusChanged;

            if (_status != null)
                _status.StatusChanged -= OnStatusChanged;
        }
    }
}
