using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
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
    /// aFiveAxis     : ST_TeachFiveAxis      bTaught, aPoint[1..n] OF (rA, rC, rX, rY, rZ)  1 + n x 40
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
    /// </remarks>
    public sealed class TwincatCalibrationService : ICalibrationService
    {
        public const string Folder = "Calibration";

        private const int Stations = 4;             // GVL_Calibration.MAX_STATIONS: the stations of a file that does not exist yet
        private const int NinePointSize = 1 + 2 * 8;
        private const int FiveAxisPointSize = 5 * 8;
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

        private CalibrationStatus _status = CalibrationStatus.NotLive;
        private int _followers;
        private int _refreshQueued;

        public TwincatCalibrationService(
            TwincatPlcConnection plc, PlcSubscriptions subscriptions, CalibrationSymbols map, string dataDirectory)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _subscriptions = subscriptions ?? throw new ArgumentNullException(nameof(subscriptions));
            _manager = (map != null ? map.Manager : null) ?? new CalibrationManagerSymbols();
            _teach = (map != null ? map.Teach : null) ?? new CalibrationTeachSymbols();
            _directory = Path.Combine(dataDirectory ?? string.Empty, Folder);

            _subscriptions.ValueChanged += OnValueChanged;
            _subscriptions.LiveChanged += (sender, e) => QueueRefresh();
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

        public IReadOnlyList<FiveAxisTeach> LoadFiveAxis()
        {
            List<FiveAxisTeach> stations = Read("FiveAxis.json", DefaultFiveAxis, t => t.Station, (t, s) => t.Station = s);
            foreach (FiveAxisTeach station in stations)
                station.Points = station.Points ?? new List<FiveAxisTeachPoint>();

            return stations;
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

        public void SaveFiveAxis(IReadOnlyList<FiveAxisTeach> stations)
        {
            Write("FiveAxis.json", stations);
        }

        public void SaveCameraNeedle(IReadOnlyList<CameraNeedleTeach> stations)
        {
            Write("CameraNeedle.json", stations);
        }

        public void SaveNeedleModule(IReadOnlyList<NeedleModuleTeach> stations)
        {
            Write("NeedleModule.json", stations);
        }

        /// <summary>The sheet's standard sweep: A-0 .. A-20 with C 0, then C-0 .. C-20 with A 0.</summary>
        private static FiveAxisTeach DefaultFiveAxis(int station)
        {
            var teach = new FiveAxisTeach { Station = station };
            for (int angle = 0; angle <= 20; angle += 5)
                teach.Points.Add(new FiveAxisTeachPoint { Name = "A-" + angle, A = angle });
            for (int angle = 0; angle <= 20; angle += 5)
                teach.Points.Add(new FiveAxisTeachPoint { Name = "C-" + angle, C = angle });

            return teach;
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
                case CalibrationKind.FiveAxis: return _teach.FiveAxis;
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

                case CalibrationKind.FiveAxis:
                    int rows = (recordSize.Value - 1) / FiveAxisPointSize;
                    if (rows < 1 || recordSize.Value != 1 + rows * FiveAxisPointSize)
                    {
                        throw new InvalidOperationException(table + "[1] is " + recordSize.Value
                            + " bytes in the PLC, which is not bTaught and rows of (rA, rC, rX, rY, rZ). The PLC type and the HMI must be changed together.");
                    }

                    foreach (FiveAxisTeach s in LoadFiveAxis())
                    {
                        if (s.Points.Count > rows)
                        {
                            throw new InvalidOperationException("station " + s.Station + " has " + s.Points.Count
                                + " 5 axis rows; the PLC holds " + rows + ".");
                        }

                        var values = new List<double>();
                        foreach (FiveAxisTeachPoint p in s.Points)
                            values.AddRange(new[] { p.A, p.C, p.X, p.Y, p.Z });
                        records.Add(Record(s.Station, recordSize.Value, s.Taught, values.ToArray()));
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

        public bool CanRun => PlcSymbolMap.Has(_manager.Select) && PlcSymbolMap.Has(_manager.Station) && PlcSymbolMap.Has(_manager.Start);

        public string CannotRunReason => CanRun
            ? string.Empty
            : "Calibration.Manager (Select, Station, Start) is blank in Data\\" + PlcSymbolMap.FileName + ": the PLC has no calibration manager.";

        public CalibrationStatus Status
        {
            get
            {
                lock (_statusGate)
                {
                    return _status;
                }
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
            if (!CanRun)
                return Task.FromException(new InvalidOperationException(CannotRunReason));

            return Task.Run(() =>
            {
                RequireLive();
                _plc.WriteSymbol(_manager.Select.Trim(), (short)kind);
                _plc.WriteSymbol(_manager.Station.Trim(), (short)station);
                Pulse(_manager.Start);
            });
        }

        public Task AbortAsync()
        {
            return PulseAsync(_manager.Abort, "Abort");
        }

        public Task ResetAsync()
        {
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
