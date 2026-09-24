using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Services;

namespace Nova.ViewModels.Pages.Audio
{
    /// <summary>
    /// The station card's service links (slide 43) and the Sensor Connection
    /// Dashboard (slides 54 and 55).
    /// </summary>
    public partial class AudioHomeViewModel
    {
        // ==================================================================
        // Service links: HIVE | MES | PDCA  (slide 43: green when connected, red when not)
        // ==================================================================

        public ObservableCollection<LinkStatusRow> Links { get; } = new ObservableCollection<LinkStatusRow>();

        private readonly Dictionary<IServiceLinkStatus, LinkStatusRow> _linkRows =
            new Dictionary<IServiceLinkStatus, LinkStatusRow>();

        /// <summary>
        /// The three links of the slide, in its order. A link with a status source shows
        /// that source's state; one without is shown not connected, with the reason in
        /// its tooltip, rather than left off the card.
        /// </summary>
        private void BuildLinkRows()
        {
            foreach (string name in new[] { "HIVE", "MES", "PDCA" })
            {
                var row = new LinkStatusRow(name, false);
                IServiceLinkStatus source = _linkSources.FirstOrDefault(
                    l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));

                if (source != null)
                {
                    row.Apply(source.State, source.Detail);
                    _linkRows[source] = row;
                    source.Changed += OnLinkChanged;
                }
                else
                {
                    row.Apply(LinkState.Disconnected, name == "HIVE"
                        ? "HIVE reporting runs only with HardwareMode=Twincat."
                        : "There is no " + name + " client in this build yet.");
                }

                Links.Add(row);
            }
        }

        /// <summary>Raised on the link's own thread; only moves scalar properties.</summary>
        private void OnLinkChanged(object sender, EventArgs e)
        {
            var source = (IServiceLinkStatus)sender;
            LinkStatusRow row;
            if (_linkRows.TryGetValue(source, out row))
                row.Apply(source.State, source.Detail);
        }


        // ==================================================================
        // Sensor Connection Dashboard  (slide 54: "Show sensor connection button after
        // login"; slide 55)
        // ==================================================================

        /// <summary>The dashboard is open in place of the Main tab's panels.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsDashboardVisible), nameof(IsSensorDashboardVisible))]
        private bool _isSensorDashboardOpen;

        /// <summary>The four panels and the station card.</summary>
        public bool IsDashboardVisible => IsMainTab && !IsSensorDashboardOpen;

        public bool IsSensorDashboardVisible => IsMainTab && IsSensorDashboardOpen;

        /// <summary>Sensors grouped by the location they watch.</summary>
        public ObservableCollection<SensorGroupRow> SensorGroups { get; } = new ObservableCollection<SensorGroupRow>();

        /// <summary>Slide 55: "Unit at location A" / "No units at location B".</summary>
        public ObservableCollection<UnitLocationRow> UnitLocations { get; } = new ObservableCollection<UnitLocationRow>();

        /// <summary>The machine's communication devices (PLC, CCDs, scanners) and whether each is connected.</summary>
        public ObservableCollection<LinkStatusRow> Devices { get; } = new ObservableCollection<LinkStatusRow>();

        public bool HasSensorService => _sensors != null;

        /// <summary>Slide 55: "Level 1: View only", "Level 2, 3: Full access".</summary>
        public string SensorAccessNote =>
            _sensors == null
                ? "Sensor states come from the PLC, which is not linked to this dashboard yet. The devices below are live."
                : CanEdit
                    ? "Click a non-critical sensor to disable or enable it. Critical sensors cannot be disabled."
                    : "Level 1: view only. Level 2 or 3 can disable non-critical sensors.";

        protected override void OnAccessLevelChanged(Nova.Abstractions.Data.AccessLevel level)
        {
            OnPropertyChanged(nameof(SensorAccessNote));

            // Slide 54: the button is shown after login, so signing out closes the dashboard.
            if (!IsLoggedIn)
                IsSensorDashboardOpen = false;
        }

        [RelayCommand]
        private void OpenSensorDashboard()
        {
            if (!IsLoggedIn)
                return;

            ApplySensors();
            IsSensorDashboardOpen = true;
        }

        [RelayCommand]
        private void CloseSensorDashboard()
        {
            IsSensorDashboardOpen = false;
        }

        /// <summary>
        /// Slide 55: "Only non-critical sensor can be disabled by manually". Asks first,
        /// sends the request, and records it (slide 44). The PLC may still refuse.
        /// </summary>
        [RelayCommand]
        private async Task ToggleSensorAsync(SensorTileRow tile)
        {
            if (tile == null || _sensors == null)
                return;

            if (!CanEdit)
            {
                _dialog?.ShowInfo("Level 2 or Level 3 is needed to disable or enable a sensor.", "Sensor " + tile.Id);
                return;
            }

            bool disable = tile.State != SensorState.Disabled;

            if (disable && tile.IsCritical)
            {
                _dialog?.ShowWarning(
                    "Sensor " + tile.Id + " (" + tile.Name + ") is critical and cannot be disabled - "
                    + "disabling it could cause a collision or a safety issue.", "Critical sensor");
                return;
            }

            string question = disable
                ? "Disable sensor " + tile.Id + " (" + tile.Name + ")?" + Environment.NewLine
                  + "The machine ignores it until it is enabled again."
                : "Enable sensor " + tile.Id + " (" + tile.Name + ") again?";

            if (_dialog != null && !_dialog.Confirm(question, disable ? "Disable sensor" : "Enable sensor"))
                return;

            bool accepted;
            try
            {
                accepted = await _sensors.SetDisabledAsync(tile.Id, disable);
            }
            catch (Exception ex)
            {
                _dialog?.ShowError("The request could not be sent: " + ex.Message, "Sensor " + tile.Id);
                return;
            }

            if (!accepted)
            {
                _dialog?.ShowWarning("The machine refused to " + (disable ? "disable" : "enable") + " sensor " + tile.Id + ".", "Sensor " + tile.Id);
                return;
            }

            LogAction((disable ? "Sensor disabled: " : "Sensor enabled: ") + tile.Id + " " + tile.Name);
            ApplySensors();
        }

        /// <summary>Built once, on the UI thread; later updates only move each tile's state.</summary>
        private void BuildSensorRows()
        {
            if (_sensors == null)
                return;

            foreach (IGrouping<string, SensorStatus> group in _sensors.ReadSensors().GroupBy(s => s.Location ?? "Other"))
            {
                var row = new SensorGroupRow(group.Key);
                foreach (SensorStatus sensor in group)
                {
                    var tile = new SensorTileRow(sensor.Id, sensor.Name, sensor.IsCritical, sensor.State);
                    row.Sensors.Add(tile);
                    _tilesById[sensor.Id] = tile;
                }

                SensorGroups.Add(row);
            }

            foreach (UnitLocationStatus location in _sensors.ReadLocations())
            {
                var row = new UnitLocationRow(location.Name, location.HasUnit);
                UnitLocations.Add(row);
                _locationsByName[location.Name] = row;
            }

            _sensors.Changed += OnSensorsChanged;
        }

        private readonly Dictionary<string, SensorTileRow> _tilesById =
            new Dictionary<string, SensorTileRow>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, UnitLocationRow> _locationsByName =
            new Dictionary<string, UnitLocationRow>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Raised on the sensor service's thread; only moves scalar properties.</summary>
        private void OnSensorsChanged(object sender, EventArgs e)
        {
            // Nothing to move while nobody is looking.
            if (IsSensorDashboardOpen)
                ApplySensors();
        }

        private void ApplySensors()
        {
            if (_sensors == null)
                return;

            foreach (SensorStatus sensor in _sensors.ReadSensors())
            {
                SensorTileRow tile;
                if (_tilesById.TryGetValue(sensor.Id, out tile))
                    tile.State = sensor.State;
            }

            foreach (UnitLocationStatus location in _sensors.ReadLocations())
            {
                UnitLocationRow row;
                if (_locationsByName.TryGetValue(location.Name, out row))
                    row.HasUnit = location.HasUnit;
            }
        }


        // ==================================================================
        // Device connections  (the comm devices behind the sensor dashboard)
        // ==================================================================

        private readonly Dictionary<string, LinkStatusRow> _devicesByName =
            new Dictionary<string, LinkStatusRow>(StringComparer.OrdinalIgnoreCase);

        private bool _devicesInitialised;

        private void OnConnectionsChanged(object sender, EventArgs e) => ApplyConnections();

        /// <summary>
        /// <see cref="IConnectionStatusService.StatusChanged"/> is raised on the poll
        /// thread, so rows are added only on the first pass (from the constructor, on the
        /// UI thread) and later passes only flip <c>IsConnected</c>. The device set is
        /// fixed once the service has started.
        /// </summary>
        private void ApplyConnections()
        {
            if (_connections == null)
                return;

            IReadOnlyList<ConnectionStatusEntry> entries = _connections.Read();
            if (entries == null)
                return;

            foreach (ConnectionStatusEntry entry in entries)
            {
                string name = entry.DeviceType + " " + entry.Name;

                LinkStatusRow row;
                if (_devicesByName.TryGetValue(name, out row))
                {
                    row.IsConnected = entry.IsConnected;
                    continue;
                }

                if (_devicesInitialised)
                    continue;

                row = new LinkStatusRow(name, entry.IsConnected);
                _devicesByName[name] = row;
                Devices.Add(row);
            }

            _devicesInitialised = true;
        }
    }

    /// <summary>One location's sensors on the sensor dashboard.</summary>
    public sealed class SensorGroupRow
    {
        public SensorGroupRow(string location)
        {
            Location = location;
        }

        public string Location { get; }

        public ObservableCollection<SensorTileRow> Sensors { get; } = new ObservableCollection<SensorTileRow>();
    }

    /// <summary>
    /// One sensor tile (slide 55). <see cref="Level"/> maps the state onto the Health
    /// colours: activated green, idle grey, manually disabled orange, signal lost red.
    /// </summary>
    public partial class SensorTileRow : ObservableObject
    {
        public SensorTileRow(string id, string name, bool isCritical, SensorState state)
        {
            Id = id;
            Name = name;
            IsCritical = isCritical;
            _state = state;
        }

        public string Id { get; }

        public string Name { get; }

        public bool IsCritical { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StateText), nameof(Level), nameof(ToolTip))]
        private SensorState _state;

        /// <summary>The slide's wording: "Sensor A-3-2 is idle".</summary>
        public string StateText =>
            State == SensorState.Activated ? "Activated"
            : State == SensorState.Disabled ? "Manually disabled"
            : State == SensorState.SignalLost ? "Signal lost"
            : "Idle";

        public HealthLevel Level =>
            State == SensorState.Activated ? HealthLevel.Normal
            : State == SensorState.Disabled ? HealthLevel.Warning
            : State == SensorState.SignalLost ? HealthLevel.Critical
            : HealthLevel.Unknown;

        public string ToolTip =>
            "Sensor " + Id + " is " + StateText.ToLowerInvariant()
            + (IsCritical ? ". Critical - cannot be disabled." : ". Non-critical - Level 2/3 can disable it.");
    }

    /// <summary>One unit position on the sensor dashboard.</summary>
    public partial class UnitLocationRow : ObservableObject
    {
        public UnitLocationRow(string name, bool hasUnit)
        {
            Name = name;
            _hasUnit = hasUnit;
        }

        public string Name { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StateText))]
        private bool _hasUnit;

        public string StateText => HasUnit ? "Unit at " + Name : "No unit at " + Name;
    }
}
