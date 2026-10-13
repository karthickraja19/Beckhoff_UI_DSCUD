using System;
using System.Collections.Generic;
using System.Threading;
using Nova.Abstractions.Services;
using Nova.Alarm;
using Teal.Library.Contracts.Models;
using Teal.Library.Log;

namespace Nova.Hive
{
    /// <summary>
    /// Puts the HIVE pieces together: alarms from the PLC decide the machine state, and
    /// every state change is sent to the gateway and remembered in LatestHiveState.ini.
    /// </summary>
    /// <remarks>
    /// This is the one class the rest of the HMI talks to. What happens where:
    /// <list type="bullet">
    /// <item>the PLC reports its machine state - <see cref="HiveStateMachine"/> turns it into the HIVE state</item>
    /// <item>an alarm starts - <see cref="HiveStateMachine"/> decides whether HIVE goes down</item>
    /// <item>an alarm is cleared - the alarm is reported to HIVE with its start and end time</item>
    /// <item>a state change - <see cref="HiveClient"/> sends it, then the state is saved to the file</item>
    /// </list>
    /// Clearing the alarm does not bring HIVE back out of downtime; only the operator can,
    /// with a badge (DFM slide 8).
    /// </remarks>
    public sealed class HiveService : IDisposable
    {
        private static readonly TimeSpan IdleCheckInterval = TimeSpan.FromSeconds(1);

        private readonly HiveSettings _settings;
        private readonly AlarmMonitor _alarms;
        private readonly HiveErrorCodes _codes;
        private readonly LatestHiveStateFile _stateFile;
        private readonly HiveStateMachine _machine;
        private readonly HiveClient _client;
        private readonly PlcMachineStateReader _plcState;   // null when no symbol is configured
        private readonly PlcUnitReader _plcUnits;           // null when no symbol is configured

        private Timer _idleTimer;

        /// <summary>Raised after the state changed, with the new state (1, 2 or 5).</summary>
        public event EventHandler<int> StateChanged;

        /// <param name="subscriptions">The HMI's shared PLC subscriptions; the machine state is pushed through them.</param>
        public HiveService(HiveSettings settings, AlarmMonitor alarms, PlcSubscriptions subscriptions)
        {
            _settings = settings;
            _alarms = alarms;
            _codes = HiveErrorCodes.Load(settings.ErrorCodeFile);
            _stateFile = new LatestHiveStateFile(settings.StateFile);
            _client = new HiveClient(settings);

            // Carry on in the state the machine was left in (sequence logic, rule #1).
            int startState = _stateFile.Read(settings.Sequence, HiveStateMachine.Idle);
            _machine = new HiveStateMachine(startState, DateTime.Now);

            if (!string.IsNullOrWhiteSpace(settings.MachineStateSymbol))
            {
                _plcState = new PlcMachineStateReader(subscriptions, settings.MachineStateSymbol);
            }

            if (!string.IsNullOrWhiteSpace(settings.UnitEnteredSymbol))
            {
                _plcUnits = new PlcUnitReader(subscriptions, settings.UnitEnteredSymbol, settings.UnitEnteredIsCounter);
                _machine.FollowsUnits = true;
            }
        }

        /// <summary>The state shown on the HIVE chip: 1 running, 2 idle, 5 downtime.</summary>
        public int CurrentState
        {
            get { return _machine.CurrentState; }
        }

        /// <summary>The alarm that put the machine into downtime, or null.</summary>
        public HiveError RootError
        {
            get { return _machine.RootError; }
        }

        /// <summary>The settings this service reports with; the HIVE Control tab switches Enabled.</summary>
        public HiveSettings Settings
        {
            get { return _settings; }
        }

        /// <summary>How one PLC alarm is reported to HIVE. The pop-up shows this.</summary>
        public HiveError ErrorFor(AlarmRecord alarm)
        {
            return _codes.Find(alarm);
        }

        public void Start()
        {
            _machine.StateChanged += OnStateChanged;
            _client.MachineStateSent += OnMachineStateSent;
            _alarms.AlarmStarted += OnAlarmStarted;
            _alarms.AlarmEnded += OnAlarmEnded;

            if (_plcState != null)
            {
                _plcState.MachineStateChanged += OnMachineStateChanged;
                _plcState.Start();
            }

            if (_plcUnits != null)
            {
                _plcUnits.UnitEntered += OnPlcUnitEntered;
                _plcUnits.Start();
            }

            _client.Start();
            _idleTimer = new Timer(_ => _machine.CheckIdle(DateTime.Now), null, IdleCheckInterval, IdleCheckInterval);
        }

        public void Stop()
        {
            _machine.StateChanged -= OnStateChanged;
            _client.MachineStateSent -= OnMachineStateSent;
            _alarms.AlarmStarted -= OnAlarmStarted;
            _alarms.AlarmEnded -= OnAlarmEnded;

            if (_plcState != null)
            {
                _plcState.MachineStateChanged -= OnMachineStateChanged;
                _plcState.Stop();
            }

            if (_plcUnits != null)
            {
                _plcUnits.UnitEntered -= OnPlcUnitEntered;
                _plcUnits.Stop();
            }

            if (_idleTimer != null)
            {
                _idleTimer.Dispose();
                _idleTimer = null;
            }

            _client.Stop();
        }

        public void Dispose()
        {
            Stop();

            if (_plcState != null)
                _plcState.Dispose();

            if (_plcUnits != null)
                _plcUnits.Dispose();

            _client.Dispose();
        }

        /// <summary>Call when a unit enters the machine: HIVE goes from idle back to running.</summary>
        public void UnitEntered()
        {
            _machine.UnitEntered(DateTime.Now);
        }

        /// <summary>
        /// The operator closed the alarm pop-up after swiping a badge. Sends the state
        /// again (5 to 5) with the badge and the wording the operator confirmed.
        /// </summary>
        public void AlarmAcknowledged(HiveError error, string errorDetail, string repairActionCode, string operatorId)
        {
            HiveError confirmed = error.Copy();
            confirmed.Detail = errorDetail;

            // The DFM appends the repair action to the error code, e.g. M03CVSO-01-09.
            if (!string.IsNullOrWhiteSpace(repairActionCode))
                confirmed.Code = error.Code + repairActionCode;

            _machine.AlarmAcknowledged(confirmed, operatorId, DateTime.Now);
        }

        /// <summary>Slide 47: the GUI Stop button was pressed and the operator swiped a badge.</summary>
        public void StopPressed(string operatorId, HiveError error)
        {
            _machine.StopPressed(error, operatorId, DateTime.Now);
        }

        /// <summary>Why this state change cannot be made by hand right now, or null when it can.</summary>
        public string CheckManualChange(int newState, HiveError reason = null)
        {
            return _machine.CheckManualChange(newState, reason);
        }

        /// <summary>
        /// The operator changed the state by hand from the HIVE chip (level 2/3 with a
        /// badge). This is the only way out of downtime.
        /// </summary>
        /// <returns>Null when the change was made; otherwise why it was refused.</returns>
        public string ChangeStateManually(int newState, string operatorId, HiveError reason = null)
        {
            return _machine.ChangeManually(newState, operatorId, DateTime.Now, reason);
        }

        private void OnMachineStateChanged(object sender, PlcMachineState state)
        {
            _machine.MachineStateChanged(state, DateTime.Now);
        }

        private void OnAlarmStarted(object sender, AlarmRecord alarm)
        {
            _machine.AlarmStarted(_codes.Find(alarm), alarm.StartTime);
        }

        private void OnAlarmEnded(object sender, AlarmRecord alarm)
        {
            if (alarm.EndTime.HasValue)
                _client.SendError(_codes.Find(alarm), alarm.StartTime, alarm.EndTime.Value);
        }

        // ------------------------------------------------------------------
        // Units processed while down: audit runs (PD-11/13/14) and the debug-unit
        // limits (slides 7 and 31). Fed by UnitProcessed from the unit hand-off.
        // ------------------------------------------------------------------

        /// <summary>Slide 31: "LIMITED TO UP TO 5x UNITS, THEN POP UP REQUIRES OSS TO SWITCH TO RUN MODE".</summary>
        public const int DebugRunUnitLimit = 5;

        /// <summary>Slide 7: under a non "PD-" code, 10x debug units may be used under the same error code.</summary>
        public const int UnplannedDebugUnitLimit = 10;

        private readonly object _unitGate = new object();
        private AuditSession _audit;
        private int _debugUnits;

        /// <summary>Raised when an audit round has its units; the operator decides how to go on.</summary>
        public event EventHandler<AuditSession> AuditRoundComplete;

        /// <summary>Raised when the debug units allowed in this downtime are used up, with what to tell the operator.</summary>
        public event EventHandler<string> DebugLimitReached;

        /// <summary>The audit run in progress (LAD, CCD repeatability, pose calibration), or null.</summary>
        public AuditSession Audit
        {
            get
            {
                lock (_unitGate)
                {
                    return _audit;
                }
            }
        }

        /// <summary>Debug units used in this downtime so far.</summary>
        public int DebugUnits
        {
            get
            {
                lock (_unitGate)
                {
                    return _debugUnits;
                }
            }
        }

        /// <summary>Starts an audit run once its planned downtime has been applied.</summary>
        public void StartAudit(AuditSession session)
        {
            lock (_unitGate)
            {
                _audit = session;
            }

            Logger.WriteLog(LogType.Info, "Hive", "Audit", "Audit run started: " + session.Describe(), "");
        }

        /// <summary>GRR: the next operator tests (Operator_ID + 1).</summary>
        public void NextAuditOperator()
        {
            lock (_unitGate)
            {
                if (_audit != null)
                    _audit.NextOperator();
            }
        }

        /// <summary>Another round with the same operator.</summary>
        public void ContinueAudit()
        {
            lock (_unitGate)
            {
                if (_audit != null)
                    _audit.ContinueSameOperator();
            }
        }

        /// <summary>
        /// One unit finished processing. Counts audit and debug units while HIVE is down;
        /// does nothing while running or idle. Call from the unit hand-off.
        /// </summary>
        public void UnitProcessed(string unitSn)
        {
            if (_machine.CurrentState != HiveStateMachine.Downtime)
                return;

            HiveError root = _machine.RootError;
            string code = root != null ? root.Code : null;
            AuditSession roundDone = null;
            string limit = null;

            lock (_unitGate)
            {
                if (_audit != null && string.Equals(_audit.Code, code, StringComparison.OrdinalIgnoreCase))
                {
                    if (_audit.CountUnit())
                        roundDone = _audit;
                }
                else if (string.Equals(code, PlannedDowntimeReason.DebugRunCode, StringComparison.OrdinalIgnoreCase))
                {
                    _debugUnits++;
                    if (_debugUnits == DebugRunUnitLimit)
                    {
                        limit = DebugRunUnitLimit + " debug units have been run in Tuning/Debug Material Run (PD-17). "
                            + "Switch the HIVE state to Running before the next unit.";
                    }
                }
                else if (!HiveStateMachine.IsPlanned(code))
                {
                    _debugUnits++;
                    if (_debugUnits == UnplannedDebugUnitLimit)
                    {
                        limit = UnplannedDebugUnitLimit + " debug units have been run under " + code
                            + ". No more debug units may be used under this error code.";
                    }
                }
            }

            if (roundDone != null)
                AuditRoundComplete?.Invoke(this, roundDone);

            if (limit != null)
            {
                Logger.WriteLog(LogType.Error, "Hive", "Debug", "[Error/Alert] " + limit + " Last unit: " + unitSn, "");
                DebugLimitReached?.Invoke(this, limit);
            }
        }

        private string _lastCode;

        /// <summary>True when this change carries a different downtime code from the last one sent.</summary>
        private bool RootCodeChanged(HiveStateChange change)
        {
            string code = change.Error != null ? change.Error.Code : null;
            bool changed = !string.Equals(code, _lastCode, StringComparison.OrdinalIgnoreCase);
            _lastCode = code;
            return changed;
        }

        /// <summary>A new state or a new downtime code starts the counts again.</summary>
        private void ResetUnitCounts(HiveStateChange change)
        {
            string code = change.Error != null ? change.Error.Code : null;
            lock (_unitGate)
            {
                _debugUnits = 0;
                if (_audit != null && (change.State != HiveStateMachine.Downtime
                                       || !string.Equals(_audit.Code, code, StringComparison.OrdinalIgnoreCase)))
                {
                    Logger.WriteLog(LogType.Info, "Hive", "Audit", "Audit run ended: " + _audit.Describe(), "");
                    _audit = null;
                }
            }
        }

        private void OnStateChanged(object sender, HiveStateChange change)
        {
            // An acknowledgement (5 to 5 with the same code) keeps the counts.
            bool codeChanged = RootCodeChanged(change);
            if (change.State != change.PreviousState || change.Error == null || codeChanged)
                ResetUnitCounts(change);

            // Slide 43: "Refresh Hash key each time machine state changes".
            IReadOnlyList<HashKeyReading> hashKeys = ReadHashKeys();
            change.HashKeys = hashKeys;

            _client.Send(change);
            StateChanged?.Invoke(this, change.State);

            // Slide 91: a hash key that cannot be collected is a severe machine failure.
            if (change.State != HiveStateMachine.Downtime)
            {
                HiveError hashError = HashKeyError(hashKeys);
                if (hashError != null)
                    ReportFault(hashError);
            }
        }

        /// <summary>
        /// A fault the HMI found itself - a hash key that cannot be read, a unit with the
        /// wrong number of pictures - put to HIVE like a machine alarm: running or idle
        /// goes to downtime with this error; already down, nothing more is sent.
        /// </summary>
        public void ReportFault(HiveError error)
        {
            if (error == null)
                return;

            Logger.WriteLog(LogType.Error, "Hive", "Fault", "[Error/Alert] " + error.Code + " " + error.Message + ": " + error.Detail, "");
            _machine.AlarmStarted(error, DateTime.Now);
        }

        private IReadOnlyList<HashKeyReading> ReadHashKeys()
        {
            if (_settings.ReadHashKeys == null)
                return null;

            try
            {
                return _settings.ReadHashKeys();
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "Hive", "HashKeys", "Reading the hash keys failed: " + ex.Message, ex.ToString());
                return null;
            }
        }

        /// <summary>
        /// Slide 91: every key that could not be collected, named in error_detail. The
        /// file paths go to the machine log, where OSS/FE/AME look for them.
        /// </summary>
        private static HiveError HashKeyError(IReadOnlyList<HashKeyReading> keys)
        {
            if (keys == null)
                return null;

            var failed = new List<string>();
            foreach (HashKeyReading key in keys)
            {
                if (key.IsConfigured && key.Value == null)
                {
                    failed.Add(key.Key);
                    Logger.WriteLog(LogType.Error, "Hive", "HashKeys",
                        "[Error/Alert] Hash key " + key.Key + " could not be collected from " + key.FilePath + ": " + key.Error, "");
                }
            }

            if (failed.Count == 0)
                return null;

            return HiveErrorCodes.Others("Hash key collection error: " + string.Join(", ", failed)
                + ". Check the file paths on the PD-15 hash key page.");
        }

        private void OnPlcUnitEntered(object sender, EventArgs e)
        {
            UnitEntered();
        }

        private void OnMachineStateSent(object sender, HiveStateChange change)
        {
            // Only after the gateway accepted it, so the file cannot claim a state HIVE never got.
            _stateFile.Save(_settings.Sequence, change.State);
        }
    }
}
