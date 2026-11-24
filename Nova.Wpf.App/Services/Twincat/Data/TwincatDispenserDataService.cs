using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Nova.Abstractions.Data.Models;
using Nova.Abstractions.Services;
using Nova.Alarm;
using Nova.Wpf.App.Services.Twincat.Mapping;
using Teal.Library.Log;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// <see cref="IDispenserDataService"/> on the PLC: the Data page's Glue and Health tabs,
    /// Home's glue panel and the glue warnings. Every PLC variable comes from the symbol map,
    /// section Glue.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><b>Weighings.</b> The PLC counts finished weighings up (Glue.Weighing.Count) and
    /// holds the last one's values; each is saved to the Glue weighing records
    /// (<see cref="MachineRecordStore"/>), which the Glue tab reads back.</item>
    /// <item><b>Cartridges and nozzles.</b> Live counters and hours per gantry, as ADS
    /// notifications. The glue SN, valve SN and when the cartridge was changed are the
    /// HMI's own (<see cref="IConsumableStore"/>, written by the planned-downtime procedures).</item>
    /// <item><b>Regressions.</b> W, B and R² per gantry and glue path from the PLC; when they
    /// were calibrated is the time the HMI first saw them change, kept in
    /// <c>Data\Settings\GlueRegressionTimes.json</c>.</item>
    /// <item><b>Settings.</b> The weighing settings and glue control settings are the HMI's
    /// (<c>Data\Settings\GlueSettings.json</c>). The weighing settings are also written to the
    /// mapped PLC variables on Save and each time the PLC comes back.</item>
    /// </list>
    /// A figure whose variable is blank in the map is left out (shown as unknown), never made up.
    /// </remarks>
    public sealed class TwincatDispenserDataService : IDispenserDataService
    {
        public const string WeighingKind = "GlueWeighing";

        public static readonly string[] WeighingColumns =
            { "Shift", "Gantry", "Path", "Speed", "WeightMg", "NominalMg", "LowerLimitMg", "UpperLimitMg" };

        private readonly TwincatPlcConnection _plc;
        private readonly PlcSubscriptions _subscriptions;
        private readonly GlueSymbols _glue;
        private readonly IConsumableStore _consumables;
        private readonly MachineRecordStore _weighings;
        private readonly Func<string> _currentShift;
        private readonly string _settingsPath;
        private readonly string _regressionTimesPath;
        private readonly PlcEventCounter _weighingCounter;
        private readonly object _gate = new object();

        private SettingsFile _settings;
        private Dictionary<string, RegressionSeen> _regressionTimes;

        public TwincatDispenserDataService(
            TwincatPlcConnection plc, PlcSubscriptions subscriptions, GlueSymbols glue, IConsumableStore consumables,
            MachineRecordStore weighings, string dataDirectory, Func<string> currentShift)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _subscriptions = subscriptions ?? throw new ArgumentNullException(nameof(subscriptions));
            _glue = glue ?? new GlueSymbols();
            _consumables = consumables;
            _weighings = weighings ?? throw new ArgumentNullException(nameof(weighings));
            _currentShift = currentShift ?? (() => string.Empty);
            _settingsPath = Path.Combine(dataDirectory ?? string.Empty, "Settings", "GlueSettings.json");
            _regressionTimesPath = Path.Combine(dataDirectory ?? string.Empty, "Settings", "GlueRegressionTimes.json");
            _weighingCounter = new PlcEventCounter(_subscriptions, _glue.Weighing?.Count, "glue weighing", OnWeighings);
        }

        /// <summary>Follows the live glue figures and the weighing counter, for the life of the HMI.</summary>
        public void Start()
        {
            foreach (GlueGantrySymbols gantry in (_glue.Gantries ?? new Dictionary<string, GlueGantrySymbols>()).Values.Where(g => g != null))
            {
                Watch(gantry.ProducedQty, typeof(int));
                Watch(gantry.LongPurgeCycles, typeof(int));
                Watch(gantry.ShortPurgeCycles, typeof(int));
                Watch(gantry.WeighingCycles, typeof(int));
                Watch(gantry.RemainingPotLifeHours, typeof(float));
                Watch(gantry.NozzleHoursLeft, typeof(float));
            }

            foreach (GlueRegressionSymbols regression in (_glue.Regressions ?? new List<GlueRegressionSymbols>()).Where(r => r != null && r.IsMapped))
            {
                Watch(regression.W, typeof(double));
                Watch(regression.B, typeof(double));
                Watch(regression.R2, typeof(double));
            }

            if (_glue.Weighing != null && _glue.Weighing.IsMapped)
                _weighingCounter.Start();

            // A PLC that restarted has lost the weighing settings: write them again.
            _subscriptions.LiveChanged += (sender, e) =>
            {
                if (_subscriptions.IsLive && _glue.WeighingSettings != null && _glue.WeighingSettings.IsMapped)
                {
                    Task.Run(() =>
                    {
                        try
                        {
                            WriteWeighingSettings(Settings().Weighing);
                        }
                        catch (Exception ex)
                        {
                            Logger.WriteLog(LogType.Error, "PLC", "Glue",
                                "The weighing settings could not be written to the PLC: " + ex.Message, ex.ToString());
                        }
                    });
                }
            };
        }

        private void Watch(string symbol, Type type)
        {
            string path = PlcSymbolMap.Of(symbol);
            if (path.Length > 0)
                _subscriptions.Watch(path, type);
        }

        // ==================================================================
        // Weighings
        // ==================================================================

        /// <summary>Runs on a worker thread: the PLC still holds the last weighing.</summary>
        private void OnWeighings(int count)
        {
            GlueWeighingSymbols w = _glue.Weighing;
            double? weight = PlcValues.Number(_plc, w.WeightMg);
            double? nominal = PlcValues.Number(_plc, w.NominalMg);
            double? lower = PlcValues.Number(_plc, w.LowerLimitMg);
            double? upper = PlcValues.Number(_plc, w.UpperLimitMg);
            if (!weight.HasValue || !nominal.HasValue || !lower.HasValue || !upper.HasValue)
                return;

            double? station = PlcValues.Number(_plc, w.Station);
            _weighings.Append(DateTime.Now, new[]
            {
                _currentShift() ?? string.Empty,
                GantryOfStation(station),
                MachineRecordStore.Number(PlcValues.Number(_plc, w.Path)),
                MachineRecordStore.Number(PlcValues.Number(_plc, w.Speed)),
                MachineRecordStore.Number(weight),
                MachineRecordStore.Number(nominal),
                MachineRecordStore.Number(lower),
                MachineRecordStore.Number(upper),
            });

            if (count > 1)
            {
                Logger.WriteLog(LogType.Error, "PLC", "Glue",
                    count + " weighings finished between two notifications; only the last one's values were still in the PLC.", string.Empty);
            }
        }

        /// <summary>The gantry name the map gives a station number; the number itself when none does.</summary>
        private string GantryOfStation(double? station)
        {
            if (!station.HasValue)
                return string.Empty;

            int number = (int)Math.Round(station.Value);
            foreach (var gantry in _glue.Gantries ?? new Dictionary<string, GlueGantrySymbols>())
            {
                if (gantry.Value != null && gantry.Value.Station == number)
                    return gantry.Key;
            }

            return "Station " + number.ToString(CultureInfo.InvariantCulture);
        }

        public Task<IReadOnlyList<GlueWeighingRecord>> GetGlueWeighingsAsync(DateTime start, DateTime end)
        {
            return Task.Run<IReadOnlyList<GlueWeighingRecord>>(() => _weighings.Query(start, end)
                .Select(row => new GlueWeighingRecord
                {
                    Time = row.Key,
                    Shift = MachineRecordStore.Field(row.Value, "Shift"),
                    Gantry = MachineRecordStore.Field(row.Value, "Gantry"),
                    Path = (int)(MachineRecordStore.ParseNumber(row.Value, "Path") ?? 0),
                    DispenseSpeed = MachineRecordStore.ParseNumber(row.Value, "Speed") ?? 0,
                    WeightMg = MachineRecordStore.ParseNumber(row.Value, "WeightMg") ?? 0,
                    NominalMg = MachineRecordStore.ParseNumber(row.Value, "NominalMg") ?? 0,
                    LowerLimitMg = MachineRecordStore.ParseNumber(row.Value, "LowerLimitMg") ?? double.NaN,
                    UpperLimitMg = MachineRecordStore.ParseNumber(row.Value, "UpperLimitMg") ?? double.NaN,
                })
                // A row cut short (a power cut mid-write) has no limits: it cannot be judged.
                .Where(r => !double.IsNaN(r.LowerLimitMg) && !double.IsNaN(r.UpperLimitMg))
                .ToList());
        }

        // ==================================================================
        // Regressions
        // ==================================================================

        public Task<IReadOnlyList<GlueRegression>> GetGlueRegressionsAsync()
        {
            return Task.Run<IReadOnlyList<GlueRegression>>(() =>
            {
                var result = new List<GlueRegression>();
                foreach (GlueRegressionSymbols map in (_glue.Regressions ?? new List<GlueRegressionSymbols>()).Where(r => r != null && r.IsMapped))
                {
                    double w;
                    double b;
                    if (!_subscriptions.TryGet(map.W.Trim(), out w) || !_subscriptions.TryGet(map.B.Trim(), out b))
                        continue;

                    double r2 = double.NaN;
                    double reported;
                    if (PlcSymbolMap.Has(map.R2) && _subscriptions.TryGet(map.R2.Trim(), out reported))
                        r2 = reported;

                    result.Add(new GlueRegression
                    {
                        Gantry = map.Gantry,
                        Path = map.Path,
                        W = w,
                        B = b,
                        R2 = r2,
                        CalibratedAt = SeenAt(map.Gantry + "/" + map.Path, w, b),
                        InputName = "Dispensing speed (mm/s)",
                    });
                }

                return result;
            });
        }

        /// <summary>When the HMI first saw this regression; a changed W or B is a new calibration.</summary>
        private DateTime? SeenAt(string key, double w, double b)
        {
            lock (_gate)
            {
                if (_regressionTimes == null)
                    _regressionTimes = ReadJson<Dictionary<string, RegressionSeen>>(_regressionTimesPath)
                                       ?? new Dictionary<string, RegressionSeen>(StringComparer.OrdinalIgnoreCase);

                RegressionSeen seen;
                if (_regressionTimes.TryGetValue(key, out seen) && seen.W == w && seen.B == b)
                    return seen.At;

                seen = new RegressionSeen { W = w, B = b, At = DateTime.Now };
                _regressionTimes[key] = seen;
                try
                {
                    WriteJson(_regressionTimesPath, _regressionTimes);
                }
                catch (Exception ex)
                {
                    Logger.WriteLog(LogType.Error, "Glue", "Regression", _regressionTimesPath + " could not be written: " + ex.Message, string.Empty);
                }

                return seen.At;
            }
        }

        // ==================================================================
        // Cartridges and nozzles
        // ==================================================================

        public Task<IReadOnlyList<GlueCartridgeStatus>> GetGlueCartridgesAsync()
        {
            var result = new List<GlueCartridgeStatus>();
            foreach (var gantry in Gantries())
            {
                GantryConsumables consumable = Consumable(gantry.Key);
                result.Add(new GlueCartridgeStatus
                {
                    Gantry = gantry.Key,
                    GlueSn = consumable != null ? consumable.GlueSn : string.Empty,
                    ChangedAt = consumable != null ? consumable.GlueChanged : null,
                    ProducedQty = Count(gantry.Value.ProducedQty),
                    LongPurgeCycles = Count(gantry.Value.LongPurgeCycles),
                    ShortPurgeCycles = Count(gantry.Value.ShortPurgeCycles),
                    WeighingCycles = Count(gantry.Value.WeighingCycles),
                    RemainingPotLifeHours = Hours(gantry.Value.RemainingPotLifeHours),
                });
            }

            return Task.FromResult<IReadOnlyList<GlueCartridgeStatus>>(result);
        }

        public Task<IReadOnlyList<NozzleHealthRecord>> GetNozzleHealthAsync()
        {
            var result = new List<NozzleHealthRecord>();
            foreach (var gantry in Gantries())
            {
                double? hours = Hours(gantry.Value.NozzleHoursLeft);
                if (!hours.HasValue)
                    continue;

                GantryConsumables consumable = Consumable(gantry.Key);
                result.Add(new NozzleHealthRecord
                {
                    Gantry = gantry.Key,
                    ValveSn = consumable != null ? consumable.ValveSn : string.Empty,
                    NozzleId = consumable != null ? consumable.NozzleSn : string.Empty,
                    Value = hours.Value,
                    LowerLimit = 0,
                    UpperLimit = gantry.Value.NozzleLifeHours > 0 ? gantry.Value.NozzleLifeHours : 168,
                });
            }

            return Task.FromResult<IReadOnlyList<NozzleHealthRecord>>(result);
        }

        private IEnumerable<KeyValuePair<string, GlueGantrySymbols>> Gantries()
        {
            return (_glue.Gantries ?? new Dictionary<string, GlueGantrySymbols>()).Where(g => g.Value != null);
        }

        private GantryConsumables Consumable(string gantry)
        {
            return _consumables?.Gantries?.FirstOrDefault(c => string.Equals(c.Gantry, gantry, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>A live counter; 0 when not mapped or not reported.</summary>
        private int Count(string symbol)
        {
            int value;
            return PlcSymbolMap.Has(symbol) && _subscriptions.TryGet(symbol.Trim(), out value) ? value : 0;
        }

        /// <summary>A live REAL; null when not mapped or not reported, so the page shows it as unknown.</summary>
        private double? Hours(string symbol)
        {
            float value;
            return PlcSymbolMap.Has(symbol) && _subscriptions.TryGet(symbol.Trim(), out value) ? value : (double?)null;
        }

        // ==================================================================
        // Settings
        // ==================================================================

        public Task<GlueWeighingSettings> GetWeighingSettingsAsync()
        {
            return Task.FromResult(Settings().Weighing.Copy());
        }

        /// <summary>Written to the PLC first (when mapped), then saved; a PLC that refuses them leaves the saved ones as they were.</summary>
        public Task SaveWeighingSettingsAsync(GlueWeighingSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            return Task.Run(() =>
            {
                if (_glue.WeighingSettings != null && _glue.WeighingSettings.IsMapped)
                    WriteWeighingSettings(settings);

                lock (_gate)
                {
                    SettingsFile file = Settings();
                    file.Weighing = settings.Copy();
                    WriteJson(_settingsPath, file);
                    _settings = file;
                }
            });
        }

        private void WriteWeighingSettings(GlueWeighingSettings settings)
        {
            if (!_plc.IsConnected)
                throw new InvalidOperationException("the PLC at " + _plc.Address + " is not reachable or not in Run");

            GlueWeighingSettingSymbols map = _glue.WeighingSettings;
            Write(map.ByTime, settings.ByTime);
            Write(map.IntervalMinutes, (short)settings.IntervalMinutes);
            Write(map.ByUnitCount, settings.ByUnitCount);
            Write(map.IntervalUnits, (short)settings.IntervalUnits);
            Write(map.AtShiftStart, settings.AtShiftStart);
            Write(map.AtShiftEnd, settings.AtShiftEnd);
            Write(map.ShotsPerWeighing, (short)settings.ShotsPerWeighing);
            Write(map.CalibrationIntervalHours, (short)settings.CalibrationIntervalHours);
        }

        private void Write(string symbol, object value)
        {
            string path = PlcSymbolMap.Of(symbol);
            if (path.Length > 0)
                _plc.WriteSymbol(path, value);
        }

        public Task<GlueControlSettings> GetGlueControlSettingsAsync()
        {
            return Task.FromResult(Settings().GlueControl.Copy());
        }

        public Task SaveGlueControlSettingsAsync(GlueControlSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            return Task.Run(() =>
            {
                lock (_gate)
                {
                    SettingsFile file = Settings();
                    file.GlueControl = settings.Copy();
                    WriteJson(_settingsPath, file);
                    _settings = file;
                }
            });
        }

        private SettingsFile Settings()
        {
            lock (_gate)
            {
                if (_settings != null)
                    return _settings;

                _settings = ReadJson<SettingsFile>(_settingsPath) ?? new SettingsFile();
                if (_settings.Weighing == null)
                    _settings.Weighing = DefaultWeighing();
                if (_settings.Weighing.CalibrationIntervalHours <= 0)
                    _settings.Weighing.CalibrationIntervalHours = DefaultWeighing().CalibrationIntervalHours;

                // The dosage rows follow the gantries of the map; one without a row starts empty.
                if (_settings.GlueControl == null)
                    _settings.GlueControl = new GlueControlSettings();
                if (_settings.GlueControl.Gantries == null)
                    _settings.GlueControl.Gantries = new List<GantryGlueDosage>();
                foreach (var gantry in Gantries())
                {
                    if (!_settings.GlueControl.Gantries.Any(g => string.Equals(g.Gantry, gantry.Key, StringComparison.OrdinalIgnoreCase)))
                        _settings.GlueControl.Gantries.Add(new GantryGlueDosage { Gantry = gantry.Key });
                }

                return _settings;
            }
        }

        /// <summary>S2.4.4.5's settings: weigh every 2 hours, 3 times, calibrate every 24 hours.</summary>
        private static GlueWeighingSettings DefaultWeighing()
        {
            return new GlueWeighingSettings
            {
                ByTime = true,
                IntervalMinutes = 120,
                ByUnitCount = false,
                IntervalUnits = 500,
                AtShiftStart = false,
                AtShiftEnd = false,
                ShotsPerWeighing = 3,
                CalibrationIntervalHours = 24,
            };
        }

        private static T ReadJson<T>(string path) where T : class
        {
            try
            {
                return File.Exists(path) ? JsonConvert.DeserializeObject<T>(File.ReadAllText(path)) : null;
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "Glue", "Settings", path + " could not be read: " + ex.Message, string.Empty);
                return null;
            }
        }

        /// <summary>Written through a temp file, so a power cut mid-save cannot leave a half-written file.</summary>
        private static void WriteJson(string path, object value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonConvert.SerializeObject(value, Formatting.Indented), new UTF8Encoding(false));
            if (File.Exists(path))
                File.Replace(temp, path, null);
            else
                File.Move(temp, path);
        }

        private sealed class SettingsFile
        {
            public GlueWeighingSettings Weighing { get; set; }

            public GlueControlSettings GlueControl { get; set; }
        }

        private sealed class RegressionSeen
        {
            public double W { get; set; }

            public double B { get; set; }

            public DateTime At { get; set; }
        }
    }
}
