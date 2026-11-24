using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Master.Configuration;
using Newtonsoft.Json;
using Nova.Abstractions.Hardware;
using Nova.Abstractions.Services;
using Nova.Alarm;
using Nova.Wpf.App.Services.Twincat.Mapping;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// <see cref="ISensorService"/> from the machine: the Sensor Connection Dashboard of
    /// DFM slide 55, over the sensor list in <c>Data\Settings\Sensors.json</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each sensor names its digital input (an <c>EInput</c> member, the Name column of
    /// Config.csv), read with the card's <c>ReadDI</c>; each unit location names the input
    /// that sees a unit there:
    /// <code>
    /// { "Sensors": [ { "Id": "A-1-2", "Name": "UC in place", "Location": "Loading", "IsCritical": true,
    ///                  "Input": "IN_UC_InPlace" } ],
    ///   "Locations": [ { "Name": "Loading", "Input": "IN_UC_InPlace" } ] }
    /// </code>
    /// What disables a sensor is not in that file: the symbol map (<c>Data\PlcSymbols.json</c>,
    /// section SensorDisable) names the PLC's ARRAY OF BOOL, one element per <c>ESensor</c>
    /// member in the enum's order (the sensor's Input is the member's name); TRUE disables it.
    /// With no array, or for an Input that is not an ESensor member, a sensor cannot be disabled.
    /// </para>
    /// <para>
    /// The inputs are read by the digital IO service, as the I/O Diagnostics page reads them:
    /// only while a screen follows them (<see cref="FollowStatus"/> - the dashboard, while it
    /// is open). The disable switches are ADS notifications. The PLC owns the interlocks: the
    /// HMI writes the request, and a critical sensor is never offered for disabling.
    /// </para>
    /// </remarks>
    public sealed class TwincatSensorService : ISensorService, ILiveStatus
    {
        private readonly IDigitalIOService _io;
        private readonly PlcSubscriptions _subscriptions;
        private readonly TwincatPlcConnection _plc;
        private readonly List<SensorEntry> _sensors;
        private readonly List<LocationEntry> _locations;

        /// <param name="disableSymbols">The symbol map's SensorDisable section: the PLC's array of BOOLs that disables the sensors, by <see cref="ESensor"/> index.</param>
        public TwincatSensorService(
            IDigitalIOService io, PlcSubscriptions subscriptions, TwincatPlcConnection plc, string listFile,
            SensorDisableSymbols disableSymbols = null)
        {
            _io = io;
            _subscriptions = subscriptions;
            _plc = plc;

            SensorList list = JsonConvert.DeserializeObject<SensorList>(File.ReadAllText(listFile)) ?? new SensorList();
            _sensors = (list.Sensors ?? new List<SensorEntry>()).Where(s => !string.IsNullOrWhiteSpace(s.Id)).ToList();
            _locations = (list.Locations ?? new List<LocationEntry>()).Where(l => !string.IsNullOrWhiteSpace(l.Name)).ToList();

            string array = disableSymbols != null ? (disableSymbols.Array ?? string.Empty).Trim() : string.Empty;
            int firstIndex = disableSymbols != null ? disableSymbols.FirstIndex : 0;

            foreach (SensorEntry sensor in _sensors)
            {
                // The sensor's Input names its EInput point (read with the card's ReadDI) and
                // its ESensor member, whose position is its element in the PLC's disable array.
                // A name that is not an EInput point leaves the sensor reading as signal lost.
                EInput point;
                if (Enum.TryParse((sensor.Input ?? string.Empty).Trim(), out point)
                    && point != EInput.Start && point != EInput.End)
                    sensor.InputPoint = (int)point;

                // The symbol map decides which sensors can be disabled: no array, or a sensor
                // that is not an ESensor member, and it cannot.
                ESensor member;
                sensor.DisableSymbol = array.Length > 0 && Enum.TryParse((sensor.Input ?? string.Empty).Trim(), out member)
                    ? array + "[" + (firstIndex + (int)member) + "]"
                    : string.Empty;
            }

            foreach (SensorEntry sensor in _sensors.Where(s => !string.IsNullOrWhiteSpace(s.DisableSymbol)))
                _subscriptions.Watch(sensor.DisableSymbol.Trim(), typeof(bool));

            _io.IOChanged += (sender, e) => Changed?.Invoke(this, EventArgs.Empty);
            _subscriptions.ValueChanged += (sender, symbol) =>
            {
                if (_sensors.Any(s => string.Equals(s.DisableSymbol, symbol, StringComparison.OrdinalIgnoreCase)))
                    Changed?.Invoke(this, EventArgs.Empty);
            };
        }

        public event EventHandler Changed;

        /// <summary>Reads the inputs while the returned handle is open (the dashboard is on screen).</summary>
        public IDisposable FollowStatus()
        {
            ILiveStatus live = _io as ILiveStatus;
            return live != null ? live.FollowStatus() : new Nothing();
        }

        public IReadOnlyList<SensorStatus> ReadSensors()
        {
            return _sensors.Select(s => new SensorStatus
            {
                Id = s.Id,
                Name = s.Name ?? s.Id,
                Location = string.IsNullOrWhiteSpace(s.Location) ? "Other" : s.Location,
                IsCritical = s.IsCritical,
                State = StateOf(s),
            }).ToList();
        }

        public IReadOnlyList<UnitLocationStatus> ReadLocations()
        {
            return _locations.Select(l => new UnitLocationStatus
            {
                Name = l.Name,
                HasUnit = !string.IsNullOrWhiteSpace(l.Input) && _io.ReadInput(l.Input),
            }).ToList();
        }

        public Task<bool> SetDisabledAsync(string sensorId, bool disabled)
        {
            SensorEntry sensor = _sensors.FirstOrDefault(s => string.Equals(s.Id, sensorId, StringComparison.OrdinalIgnoreCase));

            // Slide 55: only a non-critical sensor may be disabled by hand.
            if (sensor == null || string.IsNullOrWhiteSpace(sensor.DisableSymbol) || (disabled && sensor.IsCritical))
                return Task.FromResult(false);

            return Task.Run(() =>
            {
                try
                {
                    _plc.WriteSymbol(sensor.DisableSymbol.Trim(), disabled);
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            });
        }

        private SensorState StateOf(SensorEntry sensor)
        {
            bool disabled;
            if (!string.IsNullOrWhiteSpace(sensor.DisableSymbol) && _subscriptions.TryGet(sensor.DisableSymbol.Trim(), out disabled) && disabled)
                return SensorState.Disabled;

            if (sensor.InputPoint < 0 || !_subscriptions.IsLive)
                return SensorState.SignalLost;

            return ReadInput(sensor.InputPoint) ? SensorState.Activated : SensorState.Idle;
        }

        /// <summary>One input through the card's ReadDI (the EInput point's index), under the PLC lock.</summary>
        private bool ReadInput(int point)
        {
            lock (_plc.SyncRoot)
            {
                return _plc.IsConnected && _plc.Card.ReadDI(point);
            }
        }

        private sealed class SensorList
        {
            public List<SensorEntry> Sensors { get; set; }

            public List<LocationEntry> Locations { get; set; }
        }

        private sealed class SensorEntry
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Location { get; set; }
            public bool IsCritical { get; set; }
            public string Input { get; set; }

            /// <summary>The <see cref="EInput"/> index of <see cref="Input"/>; -1 when it names no input point.</summary>
            [JsonIgnore]
            public int InputPoint { get; set; } = -1;

            /// <summary>The element of the symbol map's disable array, never from the list file.</summary>
            [JsonIgnore]
            public string DisableSymbol { get; set; }
        }

        private sealed class LocationEntry
        {
            public string Name { get; set; }
            public string Input { get; set; }
        }

        private sealed class Nothing : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
