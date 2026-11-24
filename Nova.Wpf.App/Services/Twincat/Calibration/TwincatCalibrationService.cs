using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Master.Sequences;
using Master.Sequences.FiveAxis;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Nova.Abstractions.Services;
using Nova.Alarm;
using Nova.Wpf.App.Services.Twincat.Mapping;
using Teal.Library.Log;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// <see cref="ICalibrationService"/>: the taught calibration positions as JSON files in
    /// Data\Calibration, written into the PLC's teach tables (symbol map: Calibration.Teach,
    /// the PLC's GVL_CalTeach), and the PLC's calibration manager (Calibration.Manager,
    /// GVL_Calibration) for running a calibration.
    /// </summary>
    /// <remarks>
    /// The teach tables, as the PLC declares them - every type with pack_mode 1, so the bytes
    /// follow each other with no padding, and every array indexed by station from 1:
    /// <code>
    /// aNinePoint    : ST_TeachNinePoint     bTaught, rX, rY                              17 bytes
    /// aCameraNeedle : ST_TeachCameraNeedle  bTaught, aGlue[1..9] OF (rX, rY), aVision[1..9] 289
    /// aNeedleModule : ST_TeachNeedleModule  bTaught, rX, rY, rZ                           25
    /// </code>
    /// One station is one ADS write, so the PLC never sees half a record. The number of 5 axis
    /// rows is taken from the size the PLC declares the record with; rows the file does not
    /// have are written as zeros. After the last station the PLC is asked to store its
    /// persistent data (Save, a rising edge the PLC clears itself) and its answer is waited
    /// for (SaveBusy, then SaveDone or SaveError).
    ///
    /// The manager takes the calibration (E_Calibration) and the station, then the rising
    /// edge of Start; Abort and Reset are rising edges too. Each is held TRUE for 200 ms.
    /// Its status is followed with ADS notifications only while the section is on screen;
    /// its message, a STRING, is read once each time one of the followed values changes.
    ///
    /// The 5 axis calibration is the exception: the HMI runs it itself
    /// (Master.Sequences\FiveAxisCalibration): Start starts it, Stop stops it, and it needs
    /// neither the manager nor its symbols. While it runs, or shows a result that Reset has
    /// not cleared, <see cref="Status"/> is its status; otherwise the manager's.
    /// </remarks>
    public sealed class TwincatCalibrationService : ICalibrationService
    {
        public const string Folder = "Calibration";

        private const int Stations = 4;             // GVL_Calibration.MAX_STATIONS: the stations of a file that does not exist yet
        private const int NinePointSize = 1 + 2 * 8;
        private const int CameraNeedlePoints = 9;
        private const int CameraNeedleSize = 1 + 2 * CameraNeedlePoints * 2 * 8;
        private const int NeedleModuleSize = 1 + 3 * 8;
        private const int PulseMs = 200;
        private static readonly TimeSpan SaveTimeout = TimeSpan.FromSeconds(15);

        private readonly TwincatPlcConnection _plc;
        private readonly PlcSubscriptions _subscriptions;
        private readonly CalibrationManagerSymbols _manager;
        private readonly CalibrationTeachSymbols _teach;
        private readonly string _directory;
        private readonly object _fileGate = new object();
        private readonly object _statusGate = new object();

        private readonly FiveAxisCalibration _fiveAxis;
        private readonly TwincatFiveAxisMotion _fiveAxisMotion;

        private CalibrationStatus _status = CalibrationStatus.NotLive;
        private int _followers;
        private int _refreshQueued;

        public TwincatCalibrationService(
            TwincatPlcConnection plc, PlcSubscriptions subscriptions, CalibrationSymbols map, string dataDirectory,
            IAxisService axes, ICncAxisProvider cncAxes, CncSymbols cnc)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _subscriptions = subscriptions ?? throw new ArgumentNullException(nameof(subscriptions));
            _manager = (map != null ? map.Manager : null) ?? new CalibrationManagerSymbols();
            _teach = (map != null ? map.Teach : null) ?? new CalibrationTeachSymbols();
            _directory = Path.Combine(dataDirectory ?? string.Empty, Folder);
            _fiveAxisMotion = new TwincatFiveAxisMotion(plc, cnc);
            _fiveAxis = new FiveAxisCalibration(axes, cncAxes, _fiveAxisMotion);
            _fiveAxis.ResultReady += (sender, result) => SaveFiveAxisResult(result);

            _subscriptions.ValueChanged += OnValueChanged;
            _subscriptions.LiveChanged += (sender, e) => QueueRefresh();
            _fiveAxis.Changed += (sender, e) => StatusChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Stops the 5 axis calibration; the application is closing.</summary>
        public void StopOnExit()
        {
            _fiveAxis.StopAndWait();
        }

        // ==================================================================
        // The files
        // ==================================================================

        private sealed class TeachFile<T>
        {
            public List<T> Stations { get; set; } = new List<T>();
        }

        public IReadOnlyList<NinePointTeach> LoadNinePoint()
        {
            return Read("NinePoint.json", s => new NinePointTeach { Station = s }, t => t.Station, (t, s) => t.Station = s);
        }

        public IReadOnlyList<NeedleModuleTeach> LoadNeedleModule()
        {
            return Read("NeedleModule.json", s => new NeedleModuleTeach { Station = s }, t => t.Station, (t, s) => t.Station = s);
        }

        // 5 axis: stations 1 and 2 only. Each has a translation table (point1 .. point9: X, Y)
        // and a rotation table (point10 .. point34: A, C, X, Y, Z), each in its own file so
        // saving one tab never overwrites the other's values.
        private const int FiveAxisStations = 2;
        private const int TranslationRows = 9;
        private const int RotationRows = 25;

        public IReadOnlyList<FiveAxisTranslation> LoadFiveAxisTranslation()
        {
            List<FiveAxisTranslation> stations = FiveAxisOnly(
                Read("FiveAxisTranslation.json", s => new FiveAxisTranslation { Station = s }, t => t.Station, (t, s) => t.Station = s),
                s => new FiveAxisTranslation { Station = s },
                t => t.Station);

            foreach (FiveAxisTranslation station in stations)
                station.Points = Rows(station.Points, TranslationRows, 1, () => new FiveAxisTranslationPoint(), (p, name) => p.Name = name);

            return stations;
        }

        public IReadOnlyList<FiveAxisRotation> LoadFiveAxisRotation()
        {
            List<FiveAxisRotation> stations = FiveAxisOnly(
                Read("FiveAxisRotation.json", s => new FiveAxisRotation { Station = s }, t => t.Station, (t, s) => t.Station = s),
                s => new FiveAxisRotation { Station = s },
                t => t.Station);

            foreach (FiveAxisRotation station in stations)
                station.Points = Rows(station.Points, RotationRows, TranslationRows + 1, () => new FiveAxisRotationPoint(), (p, name) => p.Name = name);

            return stations;
        }

        /// <summary>Exactly stations 1 and 2, in that order: a station the file lacks is empty, any other is dropped.</summary>
        private static List<T> FiveAxisOnly<T>(List<T> stored, Func<int, T> empty, Func<T, int> stationOf)
            where T : class
        {
            var result = new List<T>();
            for (int station = 1; station <= FiveAxisStations; station++)
                result.Add(stored.FirstOrDefault(t => stationOf(t) == station) ?? empty(station));

            return result;
        }

        /// <summary>Exactly <paramref name="count"/> rows named point{first} ..; rows the file lacks are zeros, extra rows are dropped.</summary>
        private static List<T> Rows<T>(List<T> stored, int count, int first, Func<T> create, Action<T, string> setName)
            where T : class
        {
            var rows = new List<T>(count);
            for (int i = 0; i < count; i++)
            {
                T row = stored != null && i < stored.Count && stored[i] != null ? stored[i] : create();
                setName(row, "point" + (first + i));
                rows.Add(row);
            }

            return rows;
        }

        public IReadOnlyList<CameraNeedleTeach> LoadCameraNeedle()
        {
            List<CameraNeedleTeach> stations = Read("CameraNeedle.json", s => new CameraNeedleTeach { Station = s }, t => t.Station, (t, s) => t.Station = s);
            foreach (CameraNeedleTeach station in stations)
            {
                station.Glue = Named(station.Glue, "glue");
                station.Vision = Named(station.Vision, "vision");
            }

            return stations;
        }

        public void SaveNinePoint(IReadOnlyList<NinePointTeach> stations)
        {
            Write("NinePoint.json", stations);
        }

        public void SaveFiveAxisTranslation(IReadOnlyList<FiveAxisTranslation> stations)
        {
            Write("FiveAxisTranslation.json", stations);
        }

        public void SaveFiveAxisRotation(IReadOnlyList<FiveAxisRotation> stations)
        {
            Write("FiveAxisRotation.json", stations);
        }

        public void SaveCameraNeedle(IReadOnlyList<CameraNeedleTeach> stations)
        {
            Write("CameraNeedle.json", stations);
        }

        public void SaveNeedleModule(IReadOnlyList<NeedleModuleTeach> stations)
        {
            Write("NeedleModule.json", stations);
        }

        /// <summary>Exactly the nine points the PLC holds, named glue1..glue9 / vision1..vision9 where the file gives no name.</summary>
        private static List<TeachPoint> Named(List<TeachPoint> points, string prefix)
        {
            var result = new List<TeachPoint>(CameraNeedlePoints);
            for (int i = 0; i < CameraNeedlePoints; i++)
            {
                TeachPoint point = points != null && i < points.Count && points[i] != null ? points[i] : new TeachPoint();
                if (string.IsNullOrWhiteSpace(point.Name))
                    point.Name = prefix + (i + 1);
                result.Add(point);
            }

            return result;
        }

        /// <summary>
        /// The file's stations in station order. A file that does not exist gives
        /// <see cref="Stations"/> empty stations; one that cannot be read throws, so a Save
        /// never replaces a file the operator could not see.
        /// </summary>
        private List<T> Read<T>(string file, Func<int, T> empty, Func<T, int> stationOf, Action<T, int> setStation)
            where T : class
        {
            string path = Path.Combine(_directory, file);
            List<T> stations = null;
            lock (_fileGate)
            {
                if (File.Exists(path))
                {
                    TeachFile<T> stored;
                    try
                    {
                        stored = JsonConvert.DeserializeObject<TeachFile<T>>(File.ReadAllText(path));
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(path + " could not be read: " + ex.Message, ex);
                    }

                    stations = stored != null ? stored.Stations : null;
                }
            }

            if (stations == null || stations.Count == 0)
                return Enumerable.Range(1, Stations).Select(empty).ToList();

            stations = stations.Where(s => s != null).ToList();
            for (int i = 0; i < stations.Count; i++)
            {
                if (stationOf(stations[i]) < 1)
                    setStation(stations[i], i + 1);
            }

            return stations.OrderBy(stationOf).ToList();
        }

        private void Write<T>(string file, IReadOnlyList<T> stations)
        {
            string path = Path.Combine(_directory, file);
            string json = JsonConvert.SerializeObject(
                new TeachFile<T> { Stations = (stations ?? new T[0]).ToList() }, Formatting.Indented);

            lock (_fileGate)
            {
                Directory.CreateDirectory(_directory);
                string temporary = path + ".tmp";
                File.WriteAllText(temporary, json);
                if (File.Exists(path))
                    File.Delete(path);
                File.Move(temporary, path);
            }
        }

        // ==================================================================
        // The PLC's teach tables
        // ==================================================================

        public bool CanWrite(CalibrationKind kind)
        {
            return PlcSymbolMap.Has(TableOf(kind));
        }

        public Task WriteToPlcAsync(CalibrationKind kind)
        {
            if (!CanWrite(kind))
            {
                return Task.FromException(new InvalidOperationException(
                    "Calibration.Teach." + kind + " is blank in Data\\" + PlcSymbolMap.FileName + ": the PLC has no teach table for it."));
            }

            return Task.Run(() =>
            {
                try
                {
                    WriteTable(kind);
                }
                catch (Exception ex)
                {
                    Logger.WriteLog(LogType.Error, "PLC", "Calibration",
                        "The " + kind + " teach data could not be written to the PLC: " + ex.Message, ex.ToString());
                    throw;
                }
            });
        }

        private string TableOf(CalibrationKind kind)
        {
            switch (kind)
            {
                case CalibrationKind.NinePoint: return _teach.NinePoint;
                // 5 axis is not written to the PLC: the HMI owns its tables and runs the sequence.
                case CalibrationKind.CameraNeedle: return _teach.CameraNeedle;
                case CalibrationKind.NeedleModule: return _teach.NeedleModule;
                default: return null;
            }
        }

        private void WriteTable(CalibrationKind kind)
        {
            if (!_plc.IsConnected)
                throw new InvalidOperationException("the PLC at " + _plc.Address + " is not reachable or not in Run");

            string table = TableOf(kind).Trim();
            int? recordSize = _plc.SymbolSize(table + "[1]");
            int? tableSize = _plc.SymbolSize(table);
            if (recordSize == null || tableSize == null || recordSize.Value < 1)
                throw new InvalidOperationException(table + " is not in the PLC - check Calibration.Teach." + kind + " in Data\\" + PlcSymbolMap.FileName + ".");

            int plcStations = tableSize.Value / recordSize.Value;

            // Every record is built and checked before any is written.
            var records = new List<KeyValuePair<int, byte[]>>();
            switch (kind)
            {
                case CalibrationKind.NinePoint:
                    Expect(table, recordSize.Value, NinePointSize, "bTaught, rX, rY");
                    foreach (NinePointTeach s in LoadNinePoint())
                        records.Add(Record(s.Station, NinePointSize, s.Taught, s.X, s.Y));
                    break;

                case CalibrationKind.NeedleModule:
                    Expect(table, recordSize.Value, NeedleModuleSize, "bTaught, rX, rY, rZ");
                    foreach (NeedleModuleTeach s in LoadNeedleModule())
                        records.Add(Record(s.Station, NeedleModuleSize, s.Taught, s.X, s.Y, s.Z));
                    break;

                case CalibrationKind.CameraNeedle:
                    Expect(table, recordSize.Value, CameraNeedleSize, "bTaught, aGlue[1..9], aVision[1..9] OF (rX, rY)");
                    foreach (CameraNeedleTeach s in LoadCameraNeedle())
                    {
                        var values = new List<double>();
                        foreach (TeachPoint p in s.Glue)
                        {
                            values.Add(p.X);
                            values.Add(p.Y);
                        }
                        foreach (TeachPoint p in s.Vision)
                        {
                            values.Add(p.X);
                            values.Add(p.Y);
                        }
                        records.Add(Record(s.Station, CameraNeedleSize, s.Taught, values.ToArray()));
                    }
                    break;

            }

            if (records.Count == 0)
                throw new InvalidOperationException("the " + kind + " file has no station.");

            foreach (KeyValuePair<int, byte[]> record in records)
            {
                if (record.Key < 1 || record.Key > plcStations)
                    throw new InvalidOperationException("station " + record.Key + " is not in the PLC: " + table + " holds stations 1 to " + plcStations + ".");
            }

            foreach (KeyValuePair<int, byte[]> record in records)
                _plc.WriteBytes(table + "[" + record.Key + "]", record.Value);

            StorePersistent();

            Logger.WriteLog(LogType.Info, "PLC", "Calibration",
                "The " + kind + " teach data (" + records.Count + " stations) was written to " + table + " and stored by the PLC.", string.Empty);
        }

        private static void Expect(string table, int plcSize, int size, string members)
        {
            if (plcSize != size)
            {
                throw new InvalidOperationException(table + "[1] is " + plcSize + " bytes in the PLC but " + size
                    + " in the HMI (" + members + ", pack_mode 1). The PLC type and the HMI must be changed together.");
            }
        }

        /// <summary>One station's record: bTaught, then the LREALs in order; what is left of the record stays zero.</summary>
        private static KeyValuePair<int, byte[]> Record(int station, int size, bool taught, params double[] values)
        {
            var bytes = new byte[size];
            bytes[0] = taught ? (byte)1 : (byte)0;
            for (int i = 0; i < values.Length; i++)
                Buffer.BlockCopy(BitConverter.GetBytes(values[i]), 0, bytes, 1 + i * sizeof(double), sizeof(double));

            return new KeyValuePair<int, byte[]>(station, bytes);
        }

        /// <summary>
        /// Has the PLC write its persistent data to disk and waits for its answer. Without a
        /// Save symbol the data is in the PLC's memory only until its next controlled stop.
        /// </summary>
        private void StorePersistent()
        {
            if (!PlcSymbolMap.Has(_teach.Save))
                return;

            // The old result first, so it is not taken for this save's.
            WriteIfMapped(_teach.SaveDone, false);
            WriteIfMapped(_teach.SaveError, false);
            _plc.WriteSymbol(_teach.Save.Trim(), true);

            if (!PlcSymbolMap.Has(_teach.SaveDone))
                return;

            DateTime limit = DateTime.UtcNow + SaveTimeout;
            while (DateTime.UtcNow < limit)
            {
                Thread.Sleep(50);
                if (PlcSymbolMap.Has(_teach.SaveBusy) && ReadBool(_teach.SaveBusy))
                    continue;

                if (PlcSymbolMap.Has(_teach.SaveError) && ReadBool(_teach.SaveError))
                {
                    string id = PlcSymbolMap.Has(_teach.SaveErrorId)
                        ? " (ADS error 0x" + Convert.ToUInt32(_plc.ReadSymbol(_teach.SaveErrorId.Trim())).ToString("X") + ")"
                        : string.Empty;
                    throw new InvalidOperationException("the values are in the PLC, but it could not store its persistent data" + id + ".");
                }

                if (ReadBool(_teach.SaveDone))
                    return;
            }

            throw new InvalidOperationException("the values are in the PLC, but it did not confirm storing its persistent data within "
                + (int)SaveTimeout.TotalSeconds + " s.");
        }

        private bool ReadBool(string symbol)
        {
            return Convert.ToBoolean(_plc.ReadSymbol(symbol.Trim()));
        }

        private void WriteIfMapped(string symbol, object value)
        {
            if (PlcSymbolMap.Has(symbol))
                _plc.WriteSymbol(symbol.Trim(), value);
        }

        // ==================================================================
        // The calibration manager
        // ==================================================================

        private bool ManagerMapped => PlcSymbolMap.Has(_manager.Select) && PlcSymbolMap.Has(_manager.Station) && PlcSymbolMap.Has(_manager.Start);

        public bool CanRun(CalibrationKind kind)
        {
            return kind == CalibrationKind.FiveAxis ? _fiveAxisMotion.IsAvailable : ManagerMapped;
        }

        public string CannotRunReason(CalibrationKind kind)
        {
            if (kind == CalibrationKind.FiveAxis)
                return _fiveAxisMotion.UnavailableReason;

            return CanRun(kind)
                ? string.Empty
                : "Calibration.Manager (Select, Station, Start) is blank in Data\\" + PlcSymbolMap.FileName + ": the PLC has no calibration manager.";
        }

        public CalibrationStatus Status
        {
            get
            {
                CalibrationStatus plc;
                lock (_statusGate)
                {
                    plc = _status;
                }

                // The 5 axis calibration's status while it runs, and its result until Reset -
                // but never over a calibration the PLC is running.
                bool fiveAxisHasStatus = _fiveAxis.IsRunning || _fiveAxis.Message.Length > 0;
                if (fiveAxisHasStatus && (_fiveAxis.IsRunning || !(plc.IsLive && plc.IsBusy)))
                {
                    return new CalibrationStatus
                    {
                        IsLive = _subscriptions.IsLive,
                        NotLiveReason = _subscriptions.IsLive ? string.Empty : (_subscriptions.NotLiveReason ?? "PLC not connected"),
                        IsBusy = _fiveAxis.IsRunning,
                        IsDone = _fiveAxis.IsDone,
                        IsError = _fiveAxis.IsError,
                        Calibration = (int)CalibrationKind.FiveAxis,
                        Message = _fiveAxis.Message
                    };
                }

                return plc;
            }
        }

        public event EventHandler StatusChanged;

        public IDisposable FollowStatus()
        {
            var symbols = new List<KeyValuePair<string, Type>>();
            foreach (string flag in new[] { _manager.Busy, _manager.Done, _manager.Error })
            {
                if (PlcSymbolMap.Has(flag))
                    symbols.Add(new KeyValuePair<string, Type>(flag.Trim(), typeof(bool)));
            }
            foreach (string number in new[] { _manager.Active, _manager.ActiveStation, _manager.Step })
            {
                if (PlcSymbolMap.Has(number))
                    symbols.Add(new KeyValuePair<string, Type>(number.Trim(), typeof(short)));
            }

            IDisposable following = _subscriptions.Follow(symbols);
            Interlocked.Increment(ref _followers);
            QueueRefresh();
            return new Handle(() =>
            {
                following.Dispose();
                Interlocked.Decrement(ref _followers);
                QueueRefresh();
            });
        }

        public Task StartAsync(CalibrationKind kind, int station)
        {
            if (!CanRun(kind))
                return Task.FromException(new InvalidOperationException(CannotRunReason(kind)));

            if (kind == CalibrationKind.FiveAxis)
                return Task.Run(() => StartFiveAxis(station));

            if (_fiveAxis.IsRunning)
                return Task.FromException(new InvalidOperationException("The 5 axis calibration is running. Stop it first."));

            return Task.Run(() =>
            {
                RequireLive();

                // The last 5 axis result is old news once the PLC starts one of its own.
                _fiveAxis.Reset();

                _plc.WriteSymbol(_manager.Select.Trim(), (short)kind);
                _plc.WriteSymbol(_manager.Station.Trim(), (short)station);
                Pulse(_manager.Start);
            });
        }

        /// <summary>Start button of the 5-Axis tab: runs Master.Sequences' FiveAxisCalibration on the station's saved table.</summary>
        private void StartFiveAxis(int station)
        {
            RequireLive();

            FiveAxisTranslation translation = LoadFiveAxisTranslation().FirstOrDefault(s => s.Station == station);
            FiveAxisRotation rotation = LoadFiveAxisRotation().FirstOrDefault(s => s.Station == station);
            if (translation == null || rotation == null)
                throw new InvalidOperationException("The 5 axis calibration has stations 1 and 2 only, not " + station + ".");

            _fiveAxis.Settings = LoadFiveAxisSettings();
            _fiveAxis.Start(new FiveAxisTeach
            {
                Station = station,
                Translation = translation.Points,
                Rotation = rotation.Points
            });
        }

        /// <summary>Data\Calibration\FiveAxisSettings.json: plate pitch, image size, vision address, feed, axis names. Missing = defaults, which Start refuses.</summary>
        private FiveAxisSettings LoadFiveAxisSettings()
        {
            string path = Path.Combine(_directory, "FiveAxisSettings.json");
            lock (_fileGate)
            {
                if (!File.Exists(path))
                    return new FiveAxisSettings();

                try
                {
                    return JsonConvert.DeserializeObject<FiveAxisSettings>(File.ReadAllText(path)) ?? new FiveAxisSettings();
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(path + " could not be read: " + ex.Message, ex);
                }
            }
        }

        public FiveAxisSetup LoadFiveAxisSetup()
        {
            FiveAxisSettings settings = LoadFiveAxisSettings();
            return new FiveAxisSetup
            {
                PlateSpacingMm = settings.PlateSpacingMm,
                ImageWidth = settings.ImageWidth,
                ImageHeight = settings.ImageHeight,
            };
        }

        /// <summary>Writes the three values into FiveAxisSettings.json; every other setting in the file stays as it is.</summary>
        public void SaveFiveAxisSetup(FiveAxisSetup setup)
        {
            if (setup == null)
                throw new ArgumentNullException(nameof(setup));

            string path = Path.Combine(_directory, "FiveAxisSettings.json");
            lock (_fileGate)
            {
                JObject json;
                if (File.Exists(path))
                {
                    try
                    {
                        json = JObject.Parse(File.ReadAllText(path));
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(path + " could not be read: " + ex.Message, ex);
                    }
                }
                else
                {
                    json = JObject.FromObject(new FiveAxisSettings());
                }

                json["PlateSpacingMm"] = setup.PlateSpacingMm;
                json["ImageWidth"] = setup.ImageWidth;
                json["ImageHeight"] = setup.ImageHeight;

                Directory.CreateDirectory(_directory);
                string temporary = path + ".tmp";
                File.WriteAllText(temporary, json.ToString(Formatting.Indented));
                if (File.Exists(path))
                    File.Delete(path);
                File.Move(temporary, path);
            }
        }

        /// <summary>The finished run's numbers, FiveAxisResult_Station{n}.json: K, D, hand-eye, the seven kinematic values, E per image.</summary>
        private void SaveFiveAxisResult(FiveAxisResult result)
        {
            string path = Path.Combine(_directory, "FiveAxisResult_Station" + result.Station + ".json");
            string json = JsonConvert.SerializeObject(result, Formatting.Indented);

            lock (_fileGate)
            {
                Directory.CreateDirectory(_directory);
                string temporary = path + ".tmp";
                File.WriteAllText(temporary, json);
                if (File.Exists(path))
                    File.Delete(path);
                File.Move(temporary, path);
            }

            Logger.WriteLog(LogType.Info, "FiveAxis", "Result", "Saved " + path + ".", string.Empty);
        }

        public Task AbortAsync(CalibrationKind kind)
        {
            // Stop button: whichever tab asks, a running 5 axis calibration is what gets stopped.
            if (_fiveAxis.IsRunning)
                return Task.Run(() => _fiveAxis.Stop());

            if (kind == CalibrationKind.FiveAxis)
                return Task.CompletedTask;

            return PulseAsync(_manager.Abort, "Abort");
        }

        public Task ResetAsync(CalibrationKind kind)
        {
            _fiveAxis.Reset();
            if (kind == CalibrationKind.FiveAxis)
                return Task.CompletedTask;

            return PulseAsync(_manager.Reset, "Reset");
        }

        private Task PulseAsync(string symbol, string name)
        {
            if (!PlcSymbolMap.Has(symbol))
            {
                return Task.FromException(new InvalidOperationException(
                    "Calibration.Manager." + name + " is blank in Data\\" + PlcSymbolMap.FileName + "."));
            }

            return Task.Run(() =>
            {
                RequireLive();
                Pulse(symbol);
            });
        }

        private void RequireLive()
        {
            if (!_plc.IsConnected)
                throw new InvalidOperationException("the PLC at " + _plc.Address + " is not reachable or not in Run");
        }

        /// <summary>The manager acts on the rising edge and does not clear the bit itself.</summary>
        private void Pulse(string symbol)
        {
            _plc.WriteSymbol(symbol.Trim(), true);
            try
            {
                Thread.Sleep(PulseMs);
            }
            finally
            {
                _plc.WriteSymbol(symbol.Trim(), false);
            }
        }

        /// <summary>Raised on the ADS thread: only queues the refresh.</summary>
        private void OnValueChanged(object sender, string symbol)
        {
            if (Volatile.Read(ref _followers) < 1 || !IsStatusSymbol(symbol))
                return;

            QueueRefresh();
        }

        private bool IsStatusSymbol(string symbol)
        {
            return Same(symbol, _manager.Busy) || Same(symbol, _manager.Done) || Same(symbol, _manager.Error)
                || Same(symbol, _manager.Active) || Same(symbol, _manager.ActiveStation) || Same(symbol, _manager.Step);
        }

        private static bool Same(string a, string b)
        {
            return PlcSymbolMap.Has(b) && string.Equals(a, b.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>One refresh waiting at a time; a change that arrives while one runs queues the next.</summary>
        private void QueueRefresh()
        {
            if (Interlocked.Exchange(ref _refreshQueued, 1) == 1)
                return;

            Task.Run(() =>
            {
                Interlocked.Exchange(ref _refreshQueued, 0);
                try
                {
                    Refresh();
                }
                catch (Exception ex)
                {
                    Logger.WriteLog(LogType.Error, "PLC", "Calibration", "The calibration status could not be read: " + ex.Message, ex.ToString());
                }
            });
        }

        private void Refresh()
        {
            CalibrationStatus status;
            if (Volatile.Read(ref _followers) < 1)
            {
                status = CalibrationStatus.NotLive;
            }
            else if (!_subscriptions.IsLive)
            {
                status = new CalibrationStatus { NotLiveReason = _subscriptions.NotLiveReason ?? "PLC not connected" };
            }
            else
            {
                status = new CalibrationStatus
                {
                    IsLive = true,
                    IsBusy = Flag(_manager.Busy),
                    IsDone = Flag(_manager.Done),
                    IsError = Flag(_manager.Error),
                    Calibration = Number(_manager.Active),
                    Station = Number(_manager.ActiveStation),
                    Step = Number(_manager.Step),
                    Message = ReadMessage()
                };
            }

            lock (_statusGate)
            {
                _status = status;
            }

            StatusChanged?.Invoke(this, EventArgs.Empty);
        }

        private bool Flag(string symbol)
        {
            return PlcSymbolMap.Has(symbol) && _subscriptions.Get<bool>(symbol.Trim());
        }

        private int Number(string symbol)
        {
            return PlcSymbolMap.Has(symbol) ? _subscriptions.Get<short>(symbol.Trim()) : 0;
        }

        /// <summary>The manager's text, read once per change of its status.</summary>
        private string ReadMessage()
        {
            if (!PlcSymbolMap.Has(_manager.Message))
                return string.Empty;

            try
            {
                return Convert.ToString(_plc.ReadSymbol(_manager.Message.Trim())) ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private sealed class Handle : IDisposable
        {
            private Action _dispose;

            public Handle(Action dispose)
            {
                _dispose = dispose;
            }

            public void Dispose()
            {
                Action dispose = Interlocked.Exchange(ref _dispose, null);
                if (dispose != null)
                    dispose();
            }
        }
    }
}

