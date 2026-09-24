using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nova.Abstractions.Data;
using Nova.Abstractions.Data.Models;
using Nova.Abstractions.Data.Repositories;
using Nova.Abstractions.Services;

namespace Nova.Core.Services
{
    /// <summary>
    /// HIVE state changes for <c>HardwareMode=Simulated</c>, following the same stops
    /// as <see cref="SimulatedProductionRepository"/>: running while units flow, daily
    /// maintenance (PD-01) at the 08:00 hand-over, idle at the 20:00 one, and downtime
    /// for the longer alarm stops.
    /// </summary>
    public sealed class SimulatedHiveStateHistory : IHiveStateHistory, ISampleDataSource
    {
        public Task<IReadOnlyList<HiveStateChangeRecord>> GetStateChangesAsync(DateTime start, DateTime end)
        {
            return Task.Run<IReadOnlyList<HiveStateChangeRecord>>(() =>
            {
                DateTime now = DateTime.Now;
                if (end > now)
                    end = now;

                var changes = new List<HiveStateChangeRecord>();

                // Start an hour early, so the state at the start is known.
                foreach (SampleStop stop in SimulatedProductionRepository.Stops(start.AddHours(-1), end))
                {
                    if (stop.Start > end)
                        break;

                    HiveStateChangeRecord into = Into(stop);
                    changes.Add(into);

                    if (stop.End <= now)
                        changes.Add(new HiveStateChangeRecord { Time = stop.End, State = 1 });
                }

                changes = changes.OrderBy(c => c.Time).ToList();

                // Keep what is in the window and the last change before it.
                HiveStateChangeRecord before = changes.LastOrDefault(c => c.Time < start);
                List<HiveStateChangeRecord> result = changes.Where(c => c.Time >= start && c.Time <= end).ToList();
                if (before != null)
                    result.Insert(0, before);

                return result;
            });
        }

        private static HiveStateChangeRecord Into(SampleStop stop)
        {
            if (stop.IsShiftChange)
            {
                return stop.Start.Hour == 8
                    ? new HiveStateChangeRecord { Time = stop.Start, State = 5, Code = "PD-01" }
                    : new HiveStateChangeRecord { Time = stop.Start, State = 2 };
            }

            // A short stop never reaches downtime: the machine idles 90 s after its last
            // unit and the alarm is cleared before anyone needs to badge in.
            return (stop.End - stop.Start).TotalMinutes < 3
                ? new HiveStateChangeRecord { Time = stop.Start.AddSeconds(90), State = 2 }
                : new HiveStateChangeRecord { Time = stop.Start, State = 5, Code = SampleAlarms.For(stop).Code };
        }
    }

    /// <summary>
    /// The alarm log for <c>HardwareMode=Simulated</c>: one alarm for each of
    /// <see cref="SimulatedProductionRepository"/>'s alarm stops, with the DFM's error
    /// types (Audio SW DFM v5.4 slides 32-33). Feeds the Home page's Error Statistics
    /// and the Alarm page.
    /// </summary>
    public sealed class SimulatedAlarmLogRepository : IAlarmLogRepository, ISampleDataSource
    {
        public Task AppendAsync(AlarmData alarm)
        {
            // Rows are written by the PLC's alarm list, never by a page.
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AlarmData>> QueryAsync(DateTime startTime, DateTime endTime, string shift = null)
        {
            return Task.Run<IReadOnlyList<AlarmData>>(() =>
            {
                DateTime now = DateTime.Now;
                var alarms = new List<AlarmData>();

                foreach (SampleStop stop in SimulatedProductionRepository.Stops(startTime, endTime))
                {
                    if (stop.IsShiftChange || stop.Start < startTime || stop.Start > endTime || stop.Start > now)
                        continue;

                    SampleAlarm alarm = SampleAlarms.For(stop);
                    string alarmShift = SimulatedProductionRepository.ShiftAtTime(stop.Start);
                    if (!string.IsNullOrWhiteSpace(shift) && !string.Equals(shift, alarmShift, StringComparison.OrdinalIgnoreCase))
                        continue;

                    alarms.Add(new AlarmData
                    {
                        StartTime = stop.Start,
                        // Still standing: no end time yet, as the Alarm page expects.
                        EndTime = stop.End <= now ? stop.End : default(DateTime),
                        AlarmCode = alarm.Code,
                        AlarmType = alarm.Type,
                        AlarmDescription = alarm.Detail,
                        Shift = alarmShift
                    });
                }

                return alarms;
            });
        }
    }

    internal sealed class SampleAlarm
    {
        public SampleAlarm(string code, string type, string detail)
        {
            Code = code;
            Type = type;
            Detail = detail;
        }

        public string Code { get; }
        public string Type { get; }
        public string Detail { get; }
    }

    /// <summary>TE010-shaped alarms in the DFM's error types, picked per stop.</summary>
    internal static class SampleAlarms
    {
        private static readonly SampleAlarm[] All =
        {
            new SampleAlarm("M03GAA2-01", "Motion Error", "Station 1 Y axis move timeout"),
            new SampleAlarm("M02GAA3-02", "Motion Error", "Station 2 dispensing Z axis not in position"),
            new SampleAlarm("C01CVCY-01", "Cylinder Error", "UC loading lifter did not reach the down position"),
            new SampleAlarm("C02CVCY-03", "Cylinder Error", "Transfer conveyor stopper did not reach the up position"),
            new SampleAlarm("S02CVSO-01", "Sensor Error", "UC in-place sensor not detected at the scanning position"),
            new SampleAlarm("V01VSOO-01", "Vision Error", "CCD1 pre-check unable to find pattern"),
            new SampleAlarm("V01VSOO-02", "Vision Error", "CCD2 post-check unable to find pattern"),
            new SampleAlarm("T01CVSS-01", "Scanning Error", "Transfer conveyor barcode scan failed"),
            new SampleAlarm("G01DSOO-01", "Glue Error", "Automatic glue weight out of spec"),
            new SampleAlarm("F02SCOO-01", "Safety Error", "Front door opened during auto run"),
            new SampleAlarm("X01IPOO-01", "Traceability Error", "No answer from MES for the carrier SN"),
        };

        public static SampleAlarm For(SampleStop stop)
        {
            int index = (int)(SimulatedProductionRepository.Noise(stop.Key, 13) * All.Length) % All.Length;
            return All[index];
        }
    }

    /// <summary>
    /// Parameter values for <c>HardwareMode=Simulated</c>: for each production spec
    /// limit, a value that wanders inside the limits and now and then steps outside,
    /// so the dashboard's out-of-spec sorting can be seen.
    /// </summary>
    public sealed class SimulatedParameterValueSource : IParameterValueSource, ISampleDataSource
    {
        private readonly IMachineSettingsService _settings;

        public SimulatedParameterValueSource(IMachineSettingsService settings)
        {
            _settings = settings;
        }

        public Task<IReadOnlyDictionary<string, double>> ReadAsync(IReadOnlyCollection<string> keys)
        {
            return Task.Run<IReadOnlyDictionary<string, double>>(() =>
            {
                var limits = new Dictionary<string, AeLimitRecord>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (AeLimitRecord record in _settings.LoadAeLimits(AeLimitKind.Production))
                        limits[record.Parameter] = record;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Spec limits could not be read: " + ex.Message);
                }

                // A new value every ten seconds; the same value within them.
                long bucket = DateTime.Now.Ticks / TimeSpan.FromSeconds(10).Ticks;
                var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

                foreach (string key in keys)
                {
                    AeLimitRecord record;
                    limits.TryGetValue(key, out record);

                    double min = 0, max = 0;
                    bool hasMin = record != null && TryParse(record.Min, out min);
                    bool hasMax = record != null && TryParse(record.Max, out max);

                    double low = hasMin ? min : (hasMax ? max - 10 : 0);
                    double high = hasMax ? max : low + 10;
                    double span = high - low;

                    double position = 0.2 + 0.6 * SimulatedProductionRepository.Noise(key, bucket);

                    // About one reading in fifteen is out of spec, and stays so for a
                    // minute so it can be seen.
                    if (SimulatedProductionRepository.Noise(key, bucket / 6 + 7) < 0.07)
                        position = SimulatedProductionRepository.Noise(key, bucket / 6) < 0.5 ? -0.15 : 1.15;

                    values[key] = Math.Round(low + span * position, 3);
                }

                return values;
            });
        }

        private static bool TryParse(string text, out double value)
        {
            return double.TryParse((text ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }

    /// <summary>
    /// Sensors for <c>HardwareMode=Simulated</c>, named after TE010's sequence of
    /// operation (SOP-35401735-R0): the conveyor entry and exit sensors, in-place and
    /// presence sensors, the station working modules and the NG slide. Carriers move
    /// through them once a second.
    /// </summary>
    /// <remarks>
    /// Which sensors are critical is the machine builder's call; here the ones that
    /// find a carrier before something moves onto it are critical (slide 55's own
    /// example), and the plain counting sensors are not.
    /// </remarks>
    public sealed class SimulatedSensorService : ISensorService, ISampleDataSource, IDisposable
    {
        private sealed class Sensor
        {
            public string Id;
            public string Name;
            public string Location;
            public bool IsCritical;
            public bool Disabled;
            public bool SignalLost;
            public int Phase;
        }

        private static readonly string[] Locations =
        {
            "Scanning", "UC loading", "Station 1", "Station 2", "UC unloading", "NG slide", "Backflow"
        };

        private readonly List<Sensor> _sensors = new List<Sensor>
        {
            new Sensor { Id = "A-1-1", Name = "Connection conveyor 1 UC entry", Location = "Scanning" },
            new Sensor { Id = "A-1-2", Name = "Connection conveyor 1 UC exit", Location = "Scanning" },
            new Sensor { Id = "B-1-1", Name = "UC scanning in-place", Location = "Scanning", IsCritical = true },
            new Sensor { Id = "B-1-2", Name = "UC scanning exit", Location = "Scanning" },
            new Sensor { Id = "C-1-1", Name = "UC loading in-place", Location = "UC loading", IsCritical = true },
            new Sensor { Id = "C-1-2", Name = "UC loading lifter presence", Location = "UC loading", IsCritical = true },
            new Sensor { Id = "C-1-3", Name = "UC loading exit", Location = "UC loading" },
            new Sensor { Id = "D1-1-1", Name = "Station 1 Y axis UC presence", Location = "Station 1", IsCritical = true },
            new Sensor { Id = "D2-1-1", Name = "Station 2 Y axis UC presence", Location = "Station 2", IsCritical = true },
            new Sensor { Id = "E-1-1", Name = "Transfer intermediate in-place", Location = "UC unloading", IsCritical = true },
            new Sensor { Id = "E-1-2", Name = "Transfer intermediate exit", Location = "UC unloading" },
            new Sensor { Id = "F-1-1", Name = "UC unloading lifter presence", Location = "UC unloading", IsCritical = true },
            new Sensor { Id = "G-1-1", Name = "Connection conveyor 3 UC entry", Location = "UC unloading" },
            new Sensor { Id = "G-1-2", Name = "Connection conveyor 3 UC exit", Location = "UC unloading" },
            new Sensor { Id = "H-1-1", Name = "NG UC entry presence", Location = "NG slide" },
            new Sensor { Id = "H-1-2", Name = "NG UC exit feedback", Location = "NG slide" },
            new Sensor { Id = "I-1-1", Name = "Backflow conveyor UC entry", Location = "Backflow" },
            new Sensor { Id = "I-1-2", Name = "Backflow conveyor UC in-place", Location = "Backflow", IsCritical = true },
            // Slide 55 shows a sensor with its signal lost; this one plays that part.
            new Sensor { Id = "I-1-3", Name = "Backflow conveyor UC exit", Location = "Backflow", SignalLost = true },
        };

        private readonly object _gate = new object();
        private readonly Timer _timer;
        private int _tick;

        public SimulatedSensorService()
        {
            for (int i = 0; i < _sensors.Count; i++)
                _sensors[i].Phase = i;

            _timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        public event EventHandler Changed;

        public IReadOnlyList<SensorStatus> ReadSensors()
        {
            lock (_gate)
            {
                return _sensors.Select(s => new SensorStatus
                {
                    Id = s.Id,
                    Name = s.Name,
                    Location = s.Location,
                    IsCritical = s.IsCritical,
                    State = s.SignalLost ? SensorState.SignalLost
                        : s.Disabled ? SensorState.Disabled
                        : IsActive(s) ? SensorState.Activated
                        : SensorState.Idle
                }).ToList();
            }
        }

        public IReadOnlyList<UnitLocationStatus> ReadLocations()
        {
            lock (_gate)
            {
                return Locations.Select((name, i) => new UnitLocationStatus
                {
                    Name = name,
                    HasUnit = (name == "NG slide" || name == "Backflow")
                        ? (_tick / 7 + i) % 5 == 0
                        : (_tick / 3 + i) % 4 != 0
                }).ToList();
            }
        }

        public Task<bool> SetDisabledAsync(string sensorId, bool disabled)
        {
            lock (_gate)
            {
                Sensor sensor = _sensors.FirstOrDefault(s => string.Equals(s.Id, sensorId, StringComparison.OrdinalIgnoreCase));

                // Slide 55: "Only non-critical sensor can be disabled by manually".
                if (sensor == null || (sensor.IsCritical && disabled))
                    return Task.FromResult(false);

                sensor.Disabled = disabled;
            }

            Changed?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(true);
        }

        public void Dispose()
        {
            _timer.Dispose();
        }

        /// <summary>Presence sensors follow the carrier at their location; entry and exit sensors pulse.</summary>
        private bool IsActive(Sensor sensor)
        {
            return (_tick + sensor.Phase) % (sensor.IsCritical ? 4 : 6) < (sensor.IsCritical ? 3 : 1);
        }

        private void Tick()
        {
            lock (_gate)
            {
                _tick++;
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
