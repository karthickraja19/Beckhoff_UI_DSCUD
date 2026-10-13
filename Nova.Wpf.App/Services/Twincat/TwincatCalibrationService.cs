using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Nova.Abstractions.Services;
using Nova.Alarm;
using Teal.Library.Log;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// <see cref="ICalibrationService"/> over the PLC's calibration block: a command struct
    /// the HMI writes (App.config PlcCalibrationCommandSymbol) and the
    /// <see cref="ST_CalibrationStatus"/> the PLC writes back (PlcCalibrationStatusSymbol).
    /// The PLC runs every calibration sequence; this only hands it the station, the routine
    /// and its parameters, and presses its buttons.
    /// </summary>
    /// <remarks>
    /// The command struct, as the PLC declares it:
    /// <code>
    /// TYPE ST_CalibrationCmd :
    /// STRUCT
    ///     bStart     : BOOL;                     // rising edge: start, or resume when paused
    ///     bPause     : BOOL;                     // rising edge: pause
    ///     bStop      : BOOL;                     // rising edge: stop
    ///     bConfirmOk : BOOL;                     // rising edge: the operator accepts the check in sPrompt
    ///     bConfirmNg : BOOL;                     // rising edge: the operator rejects it
    ///     nStation   : INT;                      // 1 or 2
    ///     nRoutine   : INT;                      // E_CalibRoutine
    ///     aParam     : ARRAY[1..20] OF LREAL;    // the routine's parameters, in its order
    /// END_STRUCT
    /// END_TYPE
    /// </code>
    /// The buttons are pulsed like the indicator bar's: TRUE, then FALSE 200 ms later, as the
    /// PLC acts on the rising edge. nStation, nRoutine and aParam are written before bStart.
    /// The PLC starts only when it is homed and idle, and holds nStation / nRoutine / aParam
    /// as they were at the start for the whole run.
    ///
    /// The writes are made one at a time, in order, on a worker thread, so a PLC that does
    /// not answer never holds up the screen. The status is an ADS notification followed only
    /// while the Calibration tab is on screen.
    ///
    /// The parameters are the HMI's: saved per station and routine in
    /// Data\Settings\CalibrationParameters.json.
    /// </remarks>
    public sealed class TwincatCalibrationService : ICalibrationService
    {
        private const int ParameterCount = 20;
        private static readonly TimeSpan PulseLength = TimeSpan.FromMilliseconds(200);

        private readonly TwincatPlcConnection _plc;
        private readonly PlcSubscriptions _subscriptions;
        private readonly string _commandSymbol;
        private readonly string _statusSymbol;
        private readonly string _parameterFile;
        private readonly object _sync = new object();
        private readonly object _fileLock = new object();

        private Task _writes = Task.CompletedTask;   // the PLC writes waiting to be made, in order

        public event EventHandler StatusChanged;

        public TwincatCalibrationService(
            TwincatPlcConnection plc, PlcSubscriptions subscriptions, string commandSymbol, string statusSymbol, string dataDirectory)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _subscriptions = subscriptions ?? throw new ArgumentNullException(nameof(subscriptions));
            _commandSymbol = commandSymbol ?? string.Empty;
            _statusSymbol = statusSymbol ?? string.Empty;
            _parameterFile = Path.Combine(dataDirectory ?? string.Empty, "Settings", "CalibrationParameters.json");

            if (_statusSymbol.Length > 0)
            {
                _subscriptions.ValueChanged += OnValueChanged;
                _subscriptions.LiveChanged += OnLiveChanged;
            }
        }

        public bool IsAvailable => _commandSymbol.Length > 0 && _statusSymbol.Length > 0;

        public string UnavailableReason
        {
            get
            {
                if (IsAvailable)
                    return string.Empty;
                if (_commandSymbol.Length == 0 && _statusSymbol.Length == 0)
                    return "PlcCalibrationCommandSymbol and PlcCalibrationStatusSymbol are not set in App.config.";
                return (_commandSymbol.Length == 0 ? "PlcCalibrationCommandSymbol" : "PlcCalibrationStatusSymbol")
                    + " is not set in App.config.";
            }
        }

        public int MaxParameters => ParameterCount;

        public int MaxResults => ST_CalibrationStatus.ResultCount;

        public CalibrationStatus Status
        {
            get
            {
                if (!IsAvailable)
                    return new CalibrationStatus { NotLiveReason = UnavailableReason };

                if (!_subscriptions.IsLive)
                    return new CalibrationStatus { NotLiveReason = _subscriptions.NotLiveReason ?? "PLC not connected" };

                if (_subscriptions.IsMissing(_statusSymbol))
                {
                    return new CalibrationStatus
                    {
                        NotLiveReason = _statusSymbol + " is not in the PLC, or its size differs from ST_CalibrationStatus.",
                    };
                }

                ST_CalibrationStatus raw;
                if (!_subscriptions.TryGet(_statusSymbol, out raw))
                    return new CalibrationStatus { NotLiveReason = "Waiting for the PLC's calibration status" };

                return new CalibrationStatus
                {
                    IsLive = true,
                    State = (CalibrationState)raw.eState,
                    Station = raw.nStation,
                    Routine = raw.nRoutine,
                    Step = raw.nStep,
                    StepText = raw.sStep ?? string.Empty,
                    Prompt = raw.sPrompt ?? string.Empty,
                    ResultOk = raw.bResultOk,
                    Results = raw.aResult != null ? (IReadOnlyList<double>)raw.aResult.ToArray() : new double[0],
                };
            }
        }

        public IDisposable FollowStatus()
        {
            if (_statusSymbol.Length == 0)
                return new NoFollow();

            IDisposable following = _subscriptions.Follow(new[]
            {
                new KeyValuePair<string, Type>(_statusSymbol, typeof(ST_CalibrationStatus)),
            });
            RaiseStatusChanged();
            return following;
        }

        // ==================================================================
        // Parameters: the HMI's copy, per station and routine
        // ==================================================================

        public CalibrationParameterSet LoadParameters(int station, string routineKey)
        {
            Dictionary<string, Dictionary<string, CalibrationParameterSet>> all = ReadFile();
            Dictionary<string, CalibrationParameterSet> routines;
            CalibrationParameterSet saved;
            if (all.TryGetValue(StationKey(station), out routines)
                && routines != null
                && routines.TryGetValue(routineKey ?? string.Empty, out saved)
                && saved != null)
            {
                return saved;
            }

            return null;
        }

        public void SaveParameters(int station, string routineKey, IDictionary<string, double> values, string savedBy)
        {
            lock (_fileLock)
            {
                Dictionary<string, Dictionary<string, CalibrationParameterSet>> all = ReadFile();
                Dictionary<string, CalibrationParameterSet> routines;
                if (!all.TryGetValue(StationKey(station), out routines) || routines == null)
                    all[StationKey(station)] = routines = new Dictionary<string, CalibrationParameterSet>(StringComparer.OrdinalIgnoreCase);

                routines[routineKey] = new CalibrationParameterSet
                {
                    Values = new Dictionary<string, double>(values ?? new Dictionary<string, double>()),
                    SavedBy = savedBy ?? string.Empty,
                    SavedAt = DateTime.Now,
                };

                Directory.CreateDirectory(Path.GetDirectoryName(_parameterFile));
                string temp = _parameterFile + ".tmp";
                File.WriteAllText(temp, JsonConvert.SerializeObject(all, Formatting.Indented));
                if (File.Exists(_parameterFile))
                    File.Replace(temp, _parameterFile, null);
                else
                    File.Move(temp, _parameterFile);
            }
        }

        private Dictionary<string, Dictionary<string, CalibrationParameterSet>> ReadFile()
        {
            lock (_fileLock)
            {
                try
                {
                    if (File.Exists(_parameterFile))
                    {
                        var read = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, CalibrationParameterSet>>>(
                            File.ReadAllText(_parameterFile));
                        if (read != null)
                        {
                            return new Dictionary<string, Dictionary<string, CalibrationParameterSet>>(read, StringComparer.OrdinalIgnoreCase);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.WriteLog(LogType.Error, "Calibration", "Parameters",
                        "Could not read " + _parameterFile + ": " + ex.Message, ex.ToString());
                }

                return new Dictionary<string, Dictionary<string, CalibrationParameterSet>>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static string StationKey(int station)
        {
            return "Station" + station;
        }

        // ==================================================================
        // Commands
        // ==================================================================

        public Task StartAsync(int station, int routine, IReadOnlyList<double> parameters)
        {
            var values = new double[ParameterCount];
            if (parameters != null)
            {
                if (parameters.Count > ParameterCount)
                    throw new ArgumentException("The PLC takes " + ParameterCount + " parameters, not " + parameters.Count + ".", nameof(parameters));
                for (int i = 0; i < parameters.Count; i++)
                    values[i] = parameters[i];
            }

            return QueueWrite(() =>
            {
                Write(Member("nStation"), (short)station);
                Write(Member("nRoutine"), (short)routine);
                for (int i = 0; i < values.Length; i++)
                    Write(Member("aParam[" + (i + 1) + "]"), values[i]);
                Pulse(Member("bStart"));
            });
        }

        public Task ResumeAsync()
        {
            return QueueWrite(() => Pulse(Member("bStart")));
        }

        public Task PauseAsync()
        {
            return QueueWrite(() => Pulse(Member("bPause")));
        }

        public Task StopAsync()
        {
            return QueueWrite(() => Pulse(Member("bStop")));
        }

        public Task AnswerPromptAsync(bool accepted)
        {
            return QueueWrite(() => Pulse(Member(accepted ? "bConfirmOk" : "bConfirmNg")));
        }

        private string Member(string name)
        {
            return _commandSymbol + "." + name;
        }

        /// <summary>Sets the BOOL TRUE, then FALSE: the PLC acts on the rising edge.</summary>
        private void Pulse(string symbol)
        {
            Write(symbol, true);
            Thread.Sleep(PulseLength);
            Write(symbol, false);
        }

        private void Write(string symbol, object value)
        {
            if (!_plc.IsConnected)
                throw new InvalidOperationException("the PLC at " + _plc.Address + " is not reachable or not in Run");

            _plc.WriteSymbol(symbol, value);
        }

        /// <summary>Runs the write after every write queued before it, on a worker thread.</summary>
        private Task QueueWrite(Action write)
        {
            if (!IsAvailable)
                return Task.FromException(new InvalidOperationException(UnavailableReason));

            lock (_sync)
            {
                Task next = _writes.ContinueWith(_ => write(), TaskScheduler.Default);
                _writes = next.ContinueWith(t =>
                {
                    if (t.Exception != null)
                    {
                        Exception ex = t.Exception.GetBaseException();
                        Logger.WriteLog(LogType.Error, "PLC", "Calibration", "Calibration command could not be written: " + ex.Message, ex.ToString());
                    }
                }, TaskScheduler.Default);
                return next;
            }
        }

        // ==================================================================

        /// <summary>Runs on the ADS thread: no ADS calls, and nothing slow.</summary>
        private void OnValueChanged(object sender, string symbol)
        {
            if (string.Equals(symbol, _statusSymbol, StringComparison.OrdinalIgnoreCase))
                RaiseStatusChanged();
        }

        private void OnLiveChanged(object sender, EventArgs e)
        {
            RaiseStatusChanged();
        }

        private void RaiseStatusChanged()
        {
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }

        private sealed class NoFollow : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
