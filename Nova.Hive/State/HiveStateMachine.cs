using System;

namespace Nova.Hive
{
    /// <summary>One HIVE state change, which becomes one machine_state payload.</summary>
    public sealed class HiveStateChange
    {
        public int State { get; set; }

        public int PreviousState { get; set; }

        public DateTime Time { get; set; }

        /// <summary>Error that caused the downtime. Only filled in for state 5.</summary>
        public HiveError Error { get; set; }

        /// <summary>Badge of the operator, only when the state was changed by hand.</summary>
        public string OperatorId { get; set; }

        /// <summary>
        /// The hash keys as read when this change was sent (slide 12). Filled in by
        /// <see cref="HiveService"/>; null sends only MS_SHA1 from the settings.
        /// </summary>
        public System.Collections.Generic.IReadOnlyList<Nova.Abstractions.Services.HashKeyReading> HashKeys { get; set; }

        /// <summary>
        /// unique_machine_state_id from UMSID.dll, only while the 24-hour dry run is on (S4.1).
        /// Null leaves the key out of the payload.
        /// </summary>
        public string UniqueMachineStateId { get; set; }
    }

    /// <summary>
    /// Decides the HIVE machine state (Audio SW DFM v5.4, slides 7 and 8).
    /// </summary>
    /// <remarks>
    /// The PLC says what state the machine is in; these rules turn that, and the alarms,
    /// into the HIVE state:
    /// <list type="bullet">
    /// <item>the PLC in auto means HIVE is running</item>
    /// <item>90 seconds after the machine stops processing, HIVE goes to idle</item>
    /// <item>an alarm that stops the machine, while running or idle, goes to downtime</item>
    /// <item>another alarm while already down changes nothing - no second payload</item>
    /// <item>the PLC stopping with no alarm to explain it also goes to downtime</item>
    /// <item>leaving downtime is only possible by hand, by a level 2/3 operator with a badge</item>
    /// <item>idle is never chosen by hand (automatic only since v5.1), and unplanned downtime never becomes planned</item>
    /// <item>resetting the alarm or pressing Start does not change the HIVE state</item>
    /// </list>
    /// Nothing in here talks to the gateway or the screen, so the rules can be read on
    /// their own. Times are passed in rather than read from the clock, so they can be tested.
    /// </remarks>
    public sealed class HiveStateMachine
    {
        public const int Running = 1;
        public const int Idle = 2;
        public const int Downtime = 5;

        /// <summary>Slide 7: "No units have processed in the last 90 seconds".</summary>
        private static readonly TimeSpan IdleAfter = TimeSpan.FromSeconds(90);

        /// <summary>
        /// How long the machine may sit in alarm or stop before downtime is reported
        /// without an error code. The alarm that explains it normally arrives within a
        /// moment, and its code is the one HIVE wants.
        /// </summary>
        private static readonly TimeSpan WaitForAlarm = TimeSpan.FromSeconds(3);

        private DateTime _lastUnitTime;
        private PlcMachineState _machineState;
        private DateTime? _stoppedWithoutAlarmSince;

        /// <summary>Raised whenever the state changes and a payload has to be sent.</summary>
        public event EventHandler<HiveStateChange> StateChanged;

        public HiveStateMachine(int startState, DateTime now)
        {
            CurrentState = startState;
            _lastUnitTime = now;
        }

        public int CurrentState { get; private set; }

        /// <summary>
        /// True when the PLC reports each unit that enters (<see cref="PlcUnitReader"/>), so
        /// Running and Idle follow the units (slides 7 and 8). The PLC being in auto then no
        /// longer counts as processing: a machine waiting for units in auto goes Idle after
        /// 90 seconds, and the first unit takes it back to Running. False keeps the PLC mode
        /// as the stand-in: auto is Running, and the 90 seconds start once it leaves auto.
        /// </summary>
        public bool FollowsUnits { get; set; }

        /// <summary>
        /// Whether the last glue weighing was done by hand: true, false, or null when the PLC
        /// does not say. PD-17 may follow PD-10 only after a manual weighing (slide 31).
        /// </summary>
        public Func<bool?> LastGlueWeighingManual { get; set; }

        /// <summary>The alarm that put the machine into downtime, or null.</summary>
        public HiveError RootError { get; private set; }

        /// <summary>
        /// The PLC reported a different machine state. Auto means HIVE is running;
        /// alarm and stop mean downtime; everything else is "not processing", which
        /// becomes idle once the 90 seconds are up.
        /// </summary>
        public void MachineStateChanged(PlcMachineState state, DateTime time)
        {
            _machineState = state;

            // Downtime is only left by hand (slide 8), so the machine coming back by
            // itself - reset, Start pressed - does not change the HIVE state.
            if (CurrentState == Downtime)
                return;

            if (state == PlcMachineState.Alarm || state == PlcMachineState.Stop)
            {
                // Wait a moment for the alarm that explains it: its code is what HIVE
                // wants on the state 5 payload.
                if (_stoppedWithoutAlarmSince == null)
                    _stoppedWithoutAlarmSince = time;

                return;
            }

            _stoppedWithoutAlarmSince = null;

            if (state == PlcMachineState.AutoRunning && !FollowsUnits)
            {
                _lastUnitTime = time;
                ChangeTo(Running, time, null, null);
            }

            // Idle, pause, homing and manual are all "not processing units": HIVE stays
            // as it is until the 90 seconds in CheckIdle are up.
        }

        /// <summary>
        /// HIVE is on again after being off (manual). The time spent off counts for nothing:
        /// the 90 seconds to idle and the 3 seconds to wait for an alarm start afresh, so
        /// leaving manual never changes the state by itself. The PLC's current state is then
        /// taken in as usual.
        /// </summary>
        public void Resume(PlcMachineState state, DateTime time)
        {
            _lastUnitTime = time;
            _stoppedWithoutAlarmSince = null;
            MachineStateChanged(state, time);
        }

        /// <summary>An alarm appeared in the PLC's alarm list.</summary>
        public void AlarmStarted(HiveError error, DateTime time)
        {
            // The alarm explains the stop, so the fallback below is no longer needed.
            _stoppedWithoutAlarmSince = null;

            // A warning does not stop production, so it does not change the HIVE state.
            if (!error.StopsMachine)
                return;

            // Already down: the operator sees the new alarm in the pop-up, but HIVE keeps
            // the alarm that caused the downtime and gets no second payload.
            if (CurrentState == Downtime)
                return;

            RootError = error;
            ChangeTo(Downtime, time, error, null);
        }

        /// <summary>A unit entered the machine.</summary>
        public void UnitEntered(DateTime time)
        {
            _lastUnitTime = time;

            if (CurrentState == Idle)
                ChangeTo(Running, time, null, null);
        }

        /// <summary>
        /// Called about once a second. Moves to idle 90 seconds after the machine stopped
        /// processing, and reports downtime for a machine that stopped with no alarm.
        /// </summary>
        public void CheckIdle(DateTime now)
        {
            if (CurrentState == Downtime)
                return;

            // The machine is in alarm or stop, but no alarm arrived to explain it.
            // Report the downtime anyway rather than leaving HIVE showing production.
            if (_stoppedWithoutAlarmSince.HasValue && now - _stoppedWithoutAlarmSince.Value >= WaitForAlarm)
            {
                _stoppedWithoutAlarmSince = null;
                RootError = HiveErrorCodes.Others("The machine stopped without an alarm");
                ChangeTo(Downtime, now, RootError, null);
                return;
            }

            // While the PLC is in auto the machine is processing, so the 90 seconds only
            // start once it leaves auto - unless the PLC reports units (FollowsUnits), then
            // it is the units themselves that keep HIVE Running.
            if (_machineState == PlcMachineState.AutoRunning && !FollowsUnits)
                _lastUnitTime = now;

            if (CurrentState == Running && now - _lastUnitTime >= IdleAfter)
                ChangeTo(Idle, now, null, null);
        }

        /// <summary>
        /// Why the operator may not make this change by hand right now, or null when they
        /// may. Slide 8 gives the manual switches: into downtime from running or idle, and
        /// out of downtime to running - nothing else.
        /// </summary>
        /// <param name="reason">The downtime reason when going to downtime, otherwise null.</param>
        public string CheckManualChange(int newState, HiveError reason)
        {
            switch (newState)
            {
                case Idle:
                    // v5.1: idle is automatic only.
                    return "Idle is automatic: HIVE goes to Idle 90 seconds after the machine stops processing units.";

                case Running:
                    if (CurrentState == Running)
                        return "HIVE is already Running.";

                    if (CurrentState == Idle)
                        return "HIVE goes back to Running by itself when a unit enters the machine.";

                    return null;

                case Downtime:
                    return CurrentState == Downtime ? CheckDowntimeChange(reason) : null;

                default:
                    return "Unknown HIVE state " + newState + ".";
            }
        }

        /// <summary>
        /// Already down, the reason may change, within slides 9 and 31: unplanned
        /// downtime never becomes planned, and PD-17 only follows a reason marked "*".
        /// </summary>
        private string CheckDowntimeChange(HiveError reason)
        {
            string currentCode = RootError != null ? RootError.Code : null;
            string newCode = reason != null ? reason.Code : null;

            // Slide 75: "Any manual change of HIVE status during this period should be disabled."
            if (string.Equals(currentCode, PoseCalibrationCode, StringComparison.OrdinalIgnoreCase))
            {
                return "Dispensing pose calibration (PD-14) is running: the HIVE state cannot be changed by hand "
                    + "until it finishes. A Level 3 user ends it by changing to Running.";
            }

            if (!IsPlanned(currentCode))
            {
                if (IsPlanned(newCode))
                {
                    return "The machine is in unplanned downtime" + CodeText(currentCode)
                        + ". Changing unplanned downtime to planned downtime is prohibited.";
                }

                return "The machine is already in unplanned downtime" + CodeText(currentCode) + ".";
            }

            if (string.Equals(currentCode, newCode, StringComparison.OrdinalIgnoreCase))
                return "The machine is already in this planned downtime.";

            if (string.Equals(newCode, PlannedDowntimeReason.DebugRunCode, StringComparison.OrdinalIgnoreCase))
            {
                PlannedDowntimeReason current = PlannedDowntimeReason.Find(currentCode);
                if (current == null || !current.AllowsDebugRun)
                {
                    return "A debug run can only follow a planned downtime marked * (such as Glue Change), not "
                        + (current != null ? current.Name : currentCode) + ".";
                }

                // Slide 31: after Glue Weighing, PD-17 only once the weighing was done by hand.
                if (string.Equals(currentCode, "PD-10", StringComparison.OrdinalIgnoreCase)
                    && LastGlueWeighingManual?.Invoke() != true)
                {
                    return "A debug run can follow Glue Weighing only after a manual weighing. "
                        + (LastGlueWeighingManual?.Invoke() == false
                            ? "The last weighing was automatic."
                            : "The PLC does not report whether the last weighing was manual.");
                }
            }

            return null;
        }

        /// <summary>Dispensing Pose Calibration (slides 74-75).</summary>
        public const string PoseCalibrationCode = "PD-14";

        /// <summary>Slide 9: planned downtime is any code beginning with "PD-".</summary>
        public static bool IsPlanned(string code)
        {
            return code != null && code.StartsWith("PD-", StringComparison.OrdinalIgnoreCase);
        }

        private static string CodeText(string code)
        {
            return string.IsNullOrEmpty(code) ? "" : " (" + code + ")";
        }

        /// <summary>
        /// The operator changed the state by hand (level 2/3 with a badge). This is the
        /// only way out of downtime. While down, a new reason goes out as a 5 to 5
        /// payload, for example Glue Change followed by a Tuning/Debug Material Run.
        /// </summary>
        /// <param name="reason">The downtime reason when going to downtime, otherwise null.</param>
        /// <returns>Null when the change was made; otherwise why it was refused.</returns>
        public string ChangeManually(int newState, string operatorId, DateTime time, HiveError reason = null)
        {
            // Checked again here: the state can change while the operator is in the dialogs.
            string refused = CheckManualChange(newState, reason);
            if (refused != null)
                return refused;

            if (newState == Running)
                _lastUnitTime = time;   // do not fall straight back to idle

            RootError = newState == Downtime ? reason : null;
            ChangeTo(newState, time, reason, operatorId, alwaysSend: newState == Downtime);
            return null;
        }

        /// <summary>
        /// Slide 47: the GUI Stop button was pressed, and the operator swiped a badge
        /// before HIVE goes down. If the machine went down while the badge dialog was
        /// open - the PLC stopped and the fallback above reported it - the downtime is
        /// sent again (5 to 5) with the badge, keeping the code that put it down.
        /// </summary>
        public void StopPressed(HiveError error, string operatorId, DateTime time)
        {
            if (CurrentState == Downtime)
            {
                ChangeTo(Downtime, time, RootError, operatorId, alwaysSend: true);
                return;
            }

            _stoppedWithoutAlarmSince = null;
            RootError = error;
            ChangeTo(Downtime, time, error, operatorId);
        }

        /// <summary>
        /// The operator closed the alarm pop-up after swiping a badge. The DFM records
        /// this as a change from state 5 to state 5, carrying the badge and the wording
        /// the operator confirmed.
        /// </summary>
        public void AlarmAcknowledged(HiveError error, string operatorId, DateTime time)
        {
            RootError = error;
            ChangeTo(Downtime, time, error, operatorId, alwaysSend: true);
        }

        private void ChangeTo(int newState, DateTime time, HiveError error, string operatorId, bool alwaysSend = false)
        {
            // No repeat payloads: HIVE only wants a message when something really changed.
            if (newState == CurrentState && !alwaysSend)
                return;

            var change = new HiveStateChange
            {
                State = newState,
                PreviousState = CurrentState,
                Time = time,
                // Error information belongs on state 5 payloads only.
                Error = newState == Downtime ? error : null,
                OperatorId = operatorId,
            };

            CurrentState = newState;

            StateChanged?.Invoke(this, change);
        }
    }
}
