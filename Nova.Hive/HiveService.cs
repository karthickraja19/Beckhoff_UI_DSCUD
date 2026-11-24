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
    ///
    /// HIVE is off during manual operation (<see cref="IsSuspended"/>): while the PLC is in
    /// MANUAL or HOMING, and while a screen that drives the machine by hand is open
    /// (<see cref="SetManualScreenActive"/>; not while the PLC runs in auto or paused). An alarm
    /// or a fault raised then neither puts HIVE down nor opens the pop-up, no idle change is
    /// made, no unit changes the state, the PLC's stop request for an HMI downtime is withdrawn,
    /// and the chip shows HIVE disabled. The PLC's state is taken in, and the idle and alarm
    /// timers start afresh, when manual operation ends. An alarm still active then is
    /// reported to HIVE as if it had just started; one cleared in manual is never sent.
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

        private readonly object _manualGate = new object();
        private bool _inManual;                          // HIVE is off: the PLC is in manual / homing, or a manual screen is open
        private bool _plcInManual;                       // the PLC reports MANUAL or HOMING
        private bool _screenManual;                      // a screen that drives the machine by hand is on screen
        private PlcMachineState _lastPlcState = PlcMachineState.Idle;
        private readonly List<AlarmRecord> _heldInManual = new List<AlarmRecord>();   // started in manual, not sent yet

        /// <summary>Raised, on a worker thread, when the PLC goes into or out of MANUAL.</summary>
        public event EventHandler SuspendedChanged;

        /// <summary>
        /// An alarm HIVE has taken in: one that started outside manual, or one still active
        /// when the PLC left manual. The alarm pop-up follows this, not the alarm list.
        /// </summary>
        public event EventHandler<AlarmRecord> AlarmReported;

        /// <summary>True during manual operation (PLC in MANUAL or HOMING, or a manual screen open): HIVE is off.</summary>
        public bool IsSuspended
        {
            get
            {
                lock (_manualGate)
                {
                    return _inManual;
                }
            }
        }

        /// <summary>Raised after the state changed, with the new state (1, 2 or 5).</summary>
        public event EventHandler<int> StateChanged;

        /// <summary>
        /// Raised after each machine_state payload was queued for HIVE, with the whole change:
        /// previous state, time, error, badge and the hash keys it was sent with. 5 to 5
        /// changes (a new reason, an acknowledged alarm) are included.
        /// </summary>
        public event EventHandler<HiveStateChange> StateChangeSent;

        /// <param name="subscriptions">The HMI's shared PLC subscriptions; the machine state is pushed through them.</param>
        public HiveService(HiveSettings settings, AlarmMonitor alarms, PlcSubscriptions subscriptions)
        {
            _settings = settings;
            _alarms = alarms;
            _codes = HiveErrorCodes.Load(settings.ErrorCodeFile);
            _stateFile = new LatestHiveStateFile(settings.StateFile);
            _client = new HiveClient(settings);

            // Carry on in the state the machine was left in (sequence logic, rule #1).
            // Payloads still waiting from the last run are newer than the state file, and
            // each one rewrites the file as the gateway accepts it, so HIVE ends up in the
            // last of them: start there, or the machine and HIVE disagree.
            int startState = _client.LastQueuedState ?? _stateFile.Read(settings.Sequence, HiveStateMachine.Idle);
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

        /// <summary>Whether the last glue weighing was done by hand; null when the PLC does not say.</summary>
        public Func<bool?> LastGlueWeighingManual
        {
            get { return _machine.LastGlueWeighingManual; }
            set { _machine.LastGlueWeighingManual = value; }
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
            _client.UnitDataSent += OnUnitDataSent;
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
            _idleTimer = new Timer(_ =>
            {
                if (!IsSuspended)
                    _machine.CheckIdle(DateTime.Now);
            }, null, IdleCheckInterval, IdleCheckInterval);
        }

        public void Stop()
        {
            _machine.StateChanged -= OnStateChanged;
            _client.MachineStateSent -= OnMachineStateSent;
            _client.UnitDataSent -= OnUnitDataSent;
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
            // HIVE is off for manual operation: a unit then must not take it from idle to running.
            if (IsSuspended)
                return;

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

        /// <summary>
        /// A screen that drives the machine by hand (the Setting page's Manual tab, Calibration,
        /// the Recipe page's Gantry and CNC tabs) came on screen or went away. HIVE is off for
        /// as long as one is, from before the PLC enters manual until after it has left, so an
        /// alarm raised by a movement made there cannot put HIVE down. It is not switched off
        /// while the PLC is running the machine in auto or paused with a unit inside.
        /// </summary>
        public void SetManualScreenActive(bool active)
        {
            lock (_manualGate)
            {
                _screenManual = active;
            }

            Reevaluate(false);
        }

        private void OnMachineStateChanged(object sender, PlcMachineState state)
        {
            lock (_manualGate)
            {
                _lastPlcState = state;
                // Homing is started from the Manual page: it is manual work too.
                _plcInManual = state == PlcMachineState.Manual || state == PlcMachineState.Homing;
            }

            Reevaluate(true);
        }

        /// <summary>
        /// Works out again whether HIVE is off. While it is off the PLC's state is not taken in
        /// (it is, in one go, when HIVE is on again); alarms are held and reported then.
        /// </summary>
        private void Reevaluate(bool plcStateChanged)
        {
            bool changed;
            bool suspended;
            PlcMachineState state;
            List<AlarmRecord> resumed = null;

            lock (_manualGate)
            {
                state = _lastPlcState;
                suspended = _plcInManual
                    || (_screenManual && state != PlcMachineState.AutoRunning && state != PlcMachineState.Pause);

                changed = suspended != _inManual;
                _inManual = suspended;

                if (changed && !suspended && _heldInManual.Count > 0)
                {
                    resumed = new List<AlarmRecord>(_heldInManual);
                    _heldInManual.Clear();
                }
            }

            if (!suspended)
            {
                if (changed)
                    _machine.Resume(state, DateTime.Now);
                else if (plcStateChanged)
                    _machine.MachineStateChanged(state, DateTime.Now);
            }

            if (changed)
            {
                Logger.WriteLog(LogType.Info, "Hive", "Manual",
                    suspended ? "Manual operation: HIVE is off (no state change, no downtime, no stop request)" : "Manual operation ended: HIVE is on again", "");

                // The stop request is withdrawn for the manual work and asserted again after it,
                // if the downtime is still there.
                PublishHmiStop();
                SuspendedChanged?.Invoke(this, EventArgs.Empty);
            }

            // Faults still there after manual: HIVE hears of them now.
            if (resumed != null)
            {
                foreach (AlarmRecord alarm in resumed)
                    Report(alarm, DateTime.Now);
            }
        }

        private void OnAlarmStarted(object sender, AlarmRecord alarm)
        {
            lock (_manualGate)
            {
                if (_inManual)
                {
                    _heldInManual.Add(alarm);
                    return;
                }
            }

            Report(alarm, alarm.StartTime);
        }

        private void Report(AlarmRecord alarm, DateTime time)
        {
            // S3.5: "If the machine is down during the calibration period, the calibration
            // process needs to be restarted."
            HiveError alarmError = _codes.Find(alarm);
            AuditSession running = Audit;
            if (running != null && running.Code == AuditSession.PoseCalibrationCode && alarmError.StopsMachine)
                FailAudit("a machine alarm during the calibration (" + alarmError.Code + " " + alarmError.Detail + ")");

            _machine.AlarmStarted(alarmError, time);
            AlarmReported?.Invoke(this, alarm);
        }

        private void OnAlarmEnded(object sender, AlarmRecord alarm)
        {
            // Started and cleared in manual: HIVE never had it.
            lock (_manualGate)
            {
                if (_heldInManual.Remove(alarm))
                    return;
            }

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

        /// <summary>CCD repeatability GRR: the last 10 captures, still as operator 3 (S3.4).</summary>
        public void StartAuditFinalTen()
        {
            lock (_unitGate)
            {
                if (_audit != null)
                    _audit.StartFinalTen();
            }
        }

        /// <summary>
        /// Cancel at the end of a test: HIVE stays in the planned downtime and the run takes no
        /// more units. Another test needs Running and then the PD code again (S3.4, S3.5).
        /// </summary>
        public void FinishAudit()
        {
            AuditSession ended = null;
            lock (_unitGate)
            {
                if (_audit != null && !_audit.IsFinished)
                {
                    _audit.Finish();
                    ended = _audit;
                }
            }

            if (ended != null)
                Logger.WriteLog(LogType.Info, "Hive", "Audit", "Audit run ended by the operator: " + ended.Describe(), "");
        }

        /// <summary>
        /// The audit run cannot be uploaded any more - a machine alarm during a pose
        /// calibration means it has to be started again (S3.5).
        /// </summary>
        public void FailAudit(string why)
        {
            lock (_unitGate)
            {
                if (_audit != null)
                    _audit.Fail(why);
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

        // ------------------------------------------------------------------
        // machine_data: each finished unit (DFM S1.2)
        // ------------------------------------------------------------------

        /// <summary>
        /// Slide 33: Traceability Error, "HIVE upload unsuccessful" (X03), on the IPC. The
        /// downtime a unit goes to when the gateway does not confirm it (slide 14 rule 2).
        /// </summary>
        public const string UploadFailedCode = "X03IPOO-01";

        /// <summary>
        /// Raised, on a worker thread, with what HIVE made of a unit's machine_data:
        /// accepted, refused by the gateway's own check, or not confirmed.
        /// </summary>
        public event EventHandler<HiveUnitResult> UnitDataResult;

        /// <summary>Raised, on a worker thread, with a message for the OSS when an audit unit's upload failed.</summary>
        public event EventHandler<string> AuditUploadFailed;

        /// <summary>
        /// Sends one finished unit to HIVE as machine_data. Call from the unit hand-off,
        /// before <see cref="UnitProcessed"/>. Adds what only HIVE knows: the HIVE state the
        /// unit ran in, and the LAD keys while an audit run is on (slides 68 to 74:
        /// "audit", "Mode", "Operator_ID", "TestSeriesID").
        /// </summary>
        public void SendUnit(HiveUnitData unit)
        {
            if (unit == null)
                return;

            int state = _machine.CurrentState;
            unit.Data["hive_state"] = state;

            // S4.1: in the 24-hour dry run unit_sn, input_time and mac_addr come from UUID.dll.
            if (DryRunActive)
            {
                try
                {
                    DryRunUnit dryRun = DryRunIds.Unit(_settings.DryRunSiCode, state, _previousState);
                    unit.UnitSn = dryRun.UnitSn;
                    unit.InputTime = dryRun.InputTime;
                    unit.Serials["mac_addr"] = dryRun.MacAddress;
                }
                catch (Exception ex)
                {
                    Logger.WriteLog(LogType.Error, "Hive", "DryRun",
                        "[Error/Alert] 24-hour dry run: the unit was not sent to HIVE, because UUID.dll gave no unit_sn, input_time and mac_addr: " + ex.Message, "");
                    return;
                }
            }

            HiveError root = _machine.RootError;
            AuditSession audit = Audit;
            if (audit != null && !audit.IsFinished && state == HiveStateMachine.Downtime && root != null
                && string.Equals(audit.Code, root.Code, StringComparison.OrdinalIgnoreCase))
            {
                unit.IsAudit = true;
                foreach (KeyValuePair<string, object> key in audit.MachineDataKeys)
                    unit.Data[key.Key] = key.Value;
            }

            _client.SendUnit(unit);
        }

        private void OnUnitDataSent(object sender, HiveUnitResult result)
        {
            string sn = result.Unit != null ? result.Unit.UnitSn : string.Empty;

            switch (result.Outcome)
            {
                case HiveUnitOutcome.NotConfirmed:
                    // Slide 14: "If success message 200/201 is not received within 3 sec,
                    // machine must go into unplanned downtime to confirm HIVE operational status."
                    ReportFault(new HiveError
                    {
                        Code = UploadFailedCode,
                        Message = "Traceability Error",
                        Detail = "HIVE upload unsuccessful: unit " + sn + " was not confirmed by the HIVE gateway (" + result.Detail + ")",
                        Severity = "error",
                        StopsMachine = true,
                    });
                    break;

                case HiveUnitOutcome.Refused:
                    // HIVE has the unit, but its own check failed it (out of limits, a missing
                    // or unknown key). What the machine does with the unit is the PLC's.
                    Logger.WriteLog(LogType.Error, "Hive", "UnitData",
                        "[Error/Alert] HIVE failed unit " + sn + ": " + result.Detail, "");

                    // An audit upload that fails with any other code than 1024 / 1025 is shown
                    // to the OSS so they can troubleshoot (S3.4, S3.5).
                    if (result.Unit != null && result.Unit.IsAudit)
                        AuditUploadFailed?.Invoke(this, "Audit data of unit " + sn + " was not accepted by HIVE: " + result.Detail);
                    break;
            }

            UnitDataResult?.Invoke(this, result);
        }

        private string _lastCode;

        /// <summary>The state HIVE was in before the current one, as UUID.dll is called with it.</summary>
        private int _previousState;

        /// <summary>True while the HIVE Control tab's 24-hour dry run is on.</summary>
        private bool DryRunActive
        {
            get
            {
                try
                {
                    return _settings.DryRun != null && _settings.DryRun();
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

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
            _previousState = change.PreviousState;

            // S4.1: in the 24-hour dry run unique_machine_state_id comes from UMSID.dll, with
            // the state_change_time it was made for.
            if (DryRunActive)
            {
                try
                {
                    DryRunState dryRun = DryRunIds.MachineState(_settings.DryRunSiCode, change.State, change.PreviousState);
                    change.UniqueMachineStateId = dryRun.UniqueMachineStateId;
                    change.Time = dryRun.StateChangeTime;
                }
                catch (Exception ex)
                {
                    Logger.WriteLog(LogType.Error, "Hive", "DryRun",
                        "[Error/Alert] 24-hour dry run: this machine_state payload goes out without unique_machine_state_id, because UMSID.dll gave none: " + ex.Message, "");
                }
            }

            _client.Send(change);
            StateChanged?.Invoke(this, change.State);
            StateChangeSent?.Invoke(this, change);

            // Out of downtime by the operator's hand: the stop request ends with it.
            if (change.State != HiveStateMachine.Downtime)
                SetHmiStop(false);

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

            // HIVE is off in manual; the machine log above still has the fault.
            if (IsSuspended)
                return;

            _machine.AlarmStarted(error, DateTime.Now);

            // The PLC knows nothing of a fault the HMI found itself, so it is asked to stop
            // the machine - or HIVE would say down while the machine goes on producing.
            if (_machine.CurrentState == HiveStateMachine.Downtime && ReferenceEquals(_machine.RootError, error))
                SetHmiStop(true);
        }

        private bool _hmiStop;            // the HMI's downtime is on, so the PLC is to stop the machine
        private bool _hmiStopPublished;   // what the PLC was last told: _hmiStop, but never while HIVE is off in manual

        /// <summary>
        /// Raised, on a worker thread, with TRUE when the downtime was raised by the HMI itself
        /// (a hash key error, a unit HIVE did not confirm, a short picture count) and the PLC
        /// is to stop the machine, and FALSE once HIVE leaves downtime - or while HIVE is off
        /// for manual operation, so the PLC's stop request never gets in the way of a manual
        /// movement. It is raised TRUE again when manual operation ends, if the downtime is still on.
        /// </summary>
        public event EventHandler<bool> HmiStopChanged;

        /// <summary>True while the HMI is asking the PLC to stop the machine.</summary>
        public bool HmiStopRequested
        {
            get
            {
                lock (_unitGate)
                {
                    return _hmiStopPublished;
                }
            }
        }

        private void SetHmiStop(bool on)
        {
            lock (_unitGate)
            {
                if (_hmiStop == on)
                    return;

                _hmiStop = on;
            }

            PublishHmiStop();
        }

        private void PublishHmiStop()
        {
            bool suspended = IsSuspended;
            bool wanted;
            bool want;

            lock (_unitGate)
            {
                wanted = _hmiStop;
                want = _hmiStop && !suspended;

                if (want == _hmiStopPublished)
                    return;

                _hmiStopPublished = want;
            }

            Logger.WriteLog(LogType.Info, "Hive", "Stop", want
                ? "HIVE downtime raised by the HMI: the PLC is asked to stop the machine"
                : wanted
                    ? "HIVE is off for manual operation: the PLC's stop request is withdrawn until it ends"
                    : "HIVE left downtime: the PLC's stop request is withdrawn", "");
            HmiStopChanged?.Invoke(this, want);
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
